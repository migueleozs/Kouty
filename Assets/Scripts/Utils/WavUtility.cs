using System;
using System.IO;
using System.Text; // Required for Encoding.ASCII
using UnityEngine;

// This static class provides utility functions to convert an AudioClip into WAV file bytes.
public static class WavUtility 
{
    private const uint HeaderSize = 44; // Standard size of a WAV file header
    private const float RescaleFactor = 32767; // Max value for 16-bit signed short (PCM audio)

    // Primary method to convert an AudioClip into a byte array suitable for WAV file writing.
    // The 'clip' is expected to be already trimmed to the desired length by the caller (MicrophoneRecorder).
    public static byte[] FromAudioClip(AudioClip clip, out int length)
    {
        if (clip == null)
        {
            Debug.LogError("WavUtility: AudioClip provided is null.");
            length = 0;
            return null; // Return null if the AudioClip is invalid
        }

        // Convert the AudioClip's float samples into 16-bit PCM WAV data bytes.
        // 'buffer' will contain the raw audio data, prefixed with space for the header.
        var buffer = ConvertAudioClipToWavBytes(clip);
        
        // Calculate the total length of the WAV file (header + audio data).
        uint totalLength = (uint)buffer.Length;
        length = (int)totalLength; // Output the total length

        // Write the standard WAV header directly into the beginning of the 'buffer'.
        // 'clip.samples * clip.channels' provides the total number of audio samples (float) that were converted.
        WriteHeader(buffer, clip, totalLength, (uint)(clip.samples * clip.channels)); 

        return buffer; // Return the complete WAV byte array
    }

    // Converts the AudioClip's float samples to 16-bit PCM bytes, leaving space for the header.
    private static byte[] ConvertAudioClipToWavBytes(AudioClip clip)
    {
        // Get all float samples from the AudioClip. 
        // 'clip.samples' is the total number of samples *per channel*.
        // Multiply by 'clip.channels' to get the total number of individual float samples.
        var samples = new float[clip.samples * clip.channels];
        clip.GetData(samples, 0); // Copy data from the AudioClip into our float array

        uint samplesToWrite = (uint)samples.Length; // This is the total number of float samples (e.g., 80000 for 5s mono)

        // Allocate the byte buffer. It needs space for the header (44 bytes) plus
        // 2 bytes for each float sample (since we convert to 16-bit Int16).
        var buffer = new byte[(samplesToWrite * 2) + HeaderSize];
        var p = HeaderSize; // 'p' is the current position in 'buffer', starting after the header

        // Iterate through each float sample and convert it to a 16-bit signed integer (short).
        for (var i = 0; i < samplesToWrite; i++)
        {
            // Clamp values to ensure they are within the float range [-1, 1] before scaling.
            // This prevents potential clipping distortion if sample values go out of range.
            var value = (short)(Mathf.Clamp(samples[i], -1f, 1f) * RescaleFactor);
            
            // Convert the short (Int16) value into 2 bytes and write them to the buffer.
            // WAV files typically use little-endian byte order for sample data.
            buffer[p++] = (byte)(value >> 0); // Low byte
            buffer[p++] = (byte)(value >> 8); // High byte
        }
        
        return buffer; // Return the buffer containing the raw audio data (with header space)
    }

    // Helper method to add a byte array to the main buffer at a given offset.
    private static void AddDataToBuffer(byte[] buffer, ref uint offset, byte[] addBytes)
    {
        foreach (var b in addBytes)
        {
            buffer[offset++] = b;
        }
    }

    // Writes the standard WAV header into the provided byte buffer at the beginning.
    private static void WriteHeader(byte[] buffer, AudioClip clip, uint totalLength, uint numAudioSamples)
    {
        var hz = (uint)clip.frequency;    // Sample Rate (e.g., 16000, 44100)
        var channels = (ushort)clip.channels; // Number of Channels (1 for mono, 2 for stereo)
        var offset = 0u; // Current position in the buffer for writing header data

        // RIFF Chunk (Standard WAV container identifier)
        AddDataToBuffer(buffer, ref offset, Encoding.ASCII.GetBytes("RIFF"));         // Chunk ID: "RIFF" (4 bytes)
        AddDataToBuffer(buffer, ref offset, BitConverter.GetBytes(totalLength - 8));  // Chunk Size: File size - 8 bytes (4 bytes)
        AddDataToBuffer(buffer, ref offset, Encoding.ASCII.GetBytes("WAVE"));         // Format: "WAVE" (4 bytes)

        // FMT Sub-chunk (Audio format description)
        AddDataToBuffer(buffer, ref offset, Encoding.ASCII.GetBytes("fmt "));         // Sub-chunk 1 ID: "fmt " (4 bytes)
        AddDataToBuffer(buffer, ref offset, BitConverter.GetBytes(16u));              // Sub-chunk 1 Size: 16 (for PCM) (4 bytes)
        AddDataToBuffer(buffer, ref offset, BitConverter.GetBytes((ushort)1));        // Audio Format: 1 = PCM (2 bytes)
        AddDataToBuffer(buffer, ref offset, BitConverter.GetBytes(channels));         // Num Channels (2 bytes)
        AddDataToBuffer(buffer, ref offset, BitConverter.GetBytes(hz));               // Sample Rate (4 bytes)
        // Byte Rate: SampleRate * NumChannels * BitsPerSample/8 (e.g., 16000 * 1 * 2 = 32000)
        AddDataToBuffer(buffer, ref offset, BitConverter.GetBytes(hz * channels * 2)); 
        // Block Align: NumChannels * BitsPerSample/8 (e.g., 1 * 2 = 2)
        AddDataToBuffer(buffer, ref offset, BitConverter.GetBytes((ushort)(channels * 2))); 
        AddDataToBuffer(buffer, ref offset, BitConverter.GetBytes((ushort)16));       // Bits Per Sample: 16 bits (2 bytes)

        // DATA Sub-chunk (Contains the actual audio data)
        AddDataToBuffer(buffer, ref offset, Encoding.ASCII.GetBytes("data"));         // Sub-chunk 2 ID: "data" (4 bytes)
        // Sub-chunk 2 Size: numAudioSamples * bytes per sample (e.g., 80000 samples * 2 bytes/sample = 160000 bytes)
        AddDataToBuffer(buffer, ref offset, BitConverter.GetBytes(numAudioSamples * 2)); 
    }
}