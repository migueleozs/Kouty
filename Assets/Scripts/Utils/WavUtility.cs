using System;
using System.IO;
using System.Text; // Required for Encoding.ASCII
using UnityEngine;

public static class WavUtility // Renamed from SavWav to WavUtility for consistency with your project
{
    private const uint HeaderSize = 44;
    private const float RescaleFactor = 32767; // to convert float to Int16 (max value for 16-bit signed short)

    // This is the primary method MicrophoneRecorder will call
    public static byte[] FromAudioClip(AudioClip clip, out int length)
    {
        if (clip == null)
        {
            Debug.LogError("WavUtility: AudioClip provided is null.");
            length = 0;
            return null;
        }

        // 'clip.samples' here should now accurately reflect the trimmed duration
        // (i.e., finalRecordedPosition from MicrophoneRecorder * channels).
        // The 'trim' logic from the original SavWav is not used, as MicrophoneRecorder
        // is responsible for providing an already trimmed AudioClip.
        var buffer = ConvertAudioClipToWavBytes(clip);
        
        // The total length of the entire WAV file including header
        uint totalLength = (uint)buffer.Length;
        length = (int)totalLength;

        // Write the WAV header into the beginning of the buffer
        // Pass the actual number of audio samples (clip.samples * clip.channels) to WriteHeader
        // This 'numAudioSamples' is crucial for the WAV 'data' sub-chunk size.
        WriteHeader(buffer, clip, totalLength, (uint)(clip.samples * clip.channels)); 

        return buffer;
    }

    private static byte[] ConvertAudioClipToWavBytes(AudioClip clip)
    {
        // Get all samples from the AudioClip.
        // MicrophoneRecorder should have already prepared a 'trimmedClip',
        // so we can directly use clip.samples here as the actual length.
        var samples = new float[clip.samples * clip.channels];
        clip.GetData(samples, 0);

        uint samplesToWrite = (uint)samples.Length; // Total float samples (samples * channels)

        // Allocate buffer for header + data (each float sample becomes 2 bytes for Int16)
        var buffer = new byte[(samplesToWrite * 2) + HeaderSize];
        var p = HeaderSize; // Start writing audio data after the header (44 bytes)

        for (var i = 0; i < samplesToWrite; i++)
        {
            // Clamp values to ensure they are within the float range [-1, 1] before scaling.
            // This prevents potential clipping distortion if sample values go out of range.
            var value = (short)(Mathf.Clamp(samples[i], -1f, 1f) * RescaleFactor);
            
            // Convert short (Int16) to 2 bytes (little-endian, as is standard for WAV)
            buffer[p++] = (byte)(value >> 0); // Low byte
            buffer[p++] = (byte)(value >> 8); // High byte
        }
        
        return buffer;
    }

    // Helper to add byte arrays to the main buffer at a given offset
    private static void AddDataToBuffer(byte[] buffer, ref uint offset, byte[] addBytes)
    {
        foreach (var b in addBytes)
        {
            buffer[offset++] = b;
        }
    }

    // Writes the WAV header directly into the provided buffer
    private static void WriteHeader(byte[] buffer, AudioClip clip, uint totalLength, uint numAudioSamples)
    {
        var hz = (uint)clip.frequency;    // Sample Rate (e.g., 44100)
        var channels = (ushort)clip.channels; // Number of Channels (e.g., 1 for mono, 2 for stereo)
        var offset = 0u; // Current position in the buffer for writing header data

        // RIFF chunk
        AddDataToBuffer(buffer, ref offset, Encoding.ASCII.GetBytes("RIFF"));         // Chunk ID (4 bytes)
        AddDataToBuffer(buffer, ref offset, BitConverter.GetBytes(totalLength - 8));  // Chunk Size (file size - 8 bytes) (4 bytes)
        AddDataToBuffer(buffer, ref offset, Encoding.ASCII.GetBytes("WAVE"));         // Format (4 bytes)

        // FMT sub-chunk
        AddDataToBuffer(buffer, ref offset, Encoding.ASCII.GetBytes("fmt "));         // Sub-chunk 1 ID (4 bytes)
        AddDataToBuffer(buffer, ref offset, BitConverter.GetBytes(16u));              // Sub-chunk 1 Size (16 for PCM) (4 bytes)
        AddDataToBuffer(buffer, ref offset, BitConverter.GetBytes((ushort)1));        // Audio Format (1 = PCM) (2 bytes)
        AddDataToBuffer(buffer, ref offset, BitConverter.GetBytes(channels));         // Num Channels (2 bytes)
        AddDataToBuffer(buffer, ref offset, BitConverter.GetBytes(hz));               // Sample Rate (4 bytes)
        AddDataToBuffer(buffer, ref offset, BitConverter.GetBytes(hz * channels * 2)); // Byte Rate (SampleRate * NumChannels * BitsPerSample/8) (4 bytes)
        AddDataToBuffer(buffer, ref offset, BitConverter.GetBytes((ushort)(channels * 2))); // Block Align (NumChannels * BitsPerSample/8) (2 bytes)
        AddDataToBuffer(buffer, ref offset, BitConverter.GetBytes((ushort)16));       // Bits Per Sample (16 bits) (2 bytes)

        // DATA sub-chunk
        AddDataToBuffer(buffer, ref offset, Encoding.ASCII.GetBytes("data"));         // Sub-chunk 2 ID (4 bytes)
        // This is the size of the *actual audio data* in bytes.
        // numAudioSamples is the count of individual float samples (e.g., 220500 for 5s mono 44.1kHz),
        // so multiply by 2 because each sample becomes 2 bytes (Int16).
        AddDataToBuffer(buffer, ref offset, BitConverter.GetBytes(numAudioSamples * 2)); 
    }
}