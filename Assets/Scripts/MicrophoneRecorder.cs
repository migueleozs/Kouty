using UnityEngine;
using System.IO;
using System.Collections;
using System; // Required for Mathf

[RequireComponent(typeof(AudioSource))]
public class MicrophoneRecorder : MonoBehaviour
{
    public string fileName = "recorded_audio.wav";
    private AudioClip recordedClip;
    private bool isRecording = false;

    // We'll use this to store the highest position reached during recording.
    // This helps in diagnostics, but the final saved length will be precisely calculated.
    private int maxRecordedPosition = 0; 

    void Start()
    {
        StartCoroutine(TestRecordingFlow());
    }

    IEnumerator TestRecordingFlow()
    {
        // Start the recording process and wait for it to confirm data is flowing
        yield return StartCoroutine(StartRecordingAndConfirmData());

        if (!isRecording) 
        {
            Debug.LogError("❌ Grabación no iniciada o sin datos. Saliendo de TestRecordingFlow.");
            yield break; // Exit the coroutine if recording couldn't start or get data
        }

        Debug.Log("🎙️ Datos de micrófono detectados. Esperando la duración principal...");
        
        // Reset maxRecordedPosition for this new recording
        maxRecordedPosition = 0;

        // Start a sub-coroutine to continuously monitor the microphone position
        // and update maxRecordedPosition. This runs in parallel with the WaitForSeconds.
        StartCoroutine(MonitorMicrophonePosition());

        yield return new WaitForSeconds(5f); // Main recording duration (e.g., 5 seconds)
        
        // Now call StopRecordingAndSave() using the maxRecordedPosition we tracked
        StopRecordingAndSave(); // No longer passing maxRecordedPosition, it's used internally.
    }

    IEnumerator StartRecordingAndConfirmData()
    {
        if (Microphone.devices.Length == 0)
        {
            Debug.LogError("❌ No se detectó ningún micrófono.");
            yield break; // CORREGIDO: Cambiado de 'return;' a 'yield break;'
        }

        // Get the default microphone device. You could also pick from Microphone.devices array.
        string micDevice = null; 
        if (Microphone.devices.Length > 0)
        {
            micDevice = Microphone.devices[0]; 
            Debug.Log($"🎙 Using microphone device: {micDevice}");
        }

        Debug.Log("🎙 Empezando grabación y esperando datos...");

        // Get microphone device capabilities (min and max supported frequencies)
        int minFreq, maxFreq;
        Microphone.GetDeviceCaps(micDevice, out minFreq, out maxFreq);
        Debug.Log($"🎙 Microphone device capabilities: MinFreq={minFreq}Hz, MaxFreq={maxFreq}Hz");

        // Determine desired recording frequency. It's best to use a standard frequency like 44100 or 48000
        // if supported, but adapt to the mic's actual capabilities.
        int desiredFrequency = 44100; // Common audio CD quality
        if (desiredFrequency < minFreq || desiredFrequency > maxFreq)
        {
            Debug.LogWarning($"🎙 Desired frequency {desiredFrequency}Hz is outside device capabilities [{minFreq}-{maxFreq}]Hz. Using {maxFreq}Hz instead.");
            desiredFrequency = maxFreq; // Fallback to max supported if desired is out of range
        }

        // Start recording with a sufficient buffer (e.g., 10 seconds capacity for a 5-second recording).
        // The 'false' means it's not a looping recording.
        recordedClip = Microphone.Start(micDevice, false, 10, desiredFrequency); 
        isRecording = true;

        // IMPORTANT: Log the actual properties of the AudioClip Unity created for the microphone.
        // These are the real characteristics of the recorded audio buffer.
        int actualChannels = recordedClip.channels;
        int actualFrequency = recordedClip.frequency;
        Debug.Log($"🎙 AudioClip created with Frequency: {actualFrequency}Hz, Channels: {actualChannels}");

        int attempts = 0;
        const int maxAttempts = 200; // Try for up to 200 frames (approx 3-4 seconds at 60fps)

        // Wait until Microphone.GetPosition returns a positive value (meaning data is flowing into the buffer)
        // or until maxAttempts is reached (to prevent infinite loop if no mic input)
        while (Microphone.GetPosition(null) <= 0 && attempts < maxAttempts)
        {
            attempts++;
            yield return null; // Wait for the next frame
        }

        if (Microphone.GetPosition(null) <= 0)
        {
            Debug.LogError("❌ Error: Micrófono no detectó audio después de múltiples intentos.");
            Microphone.End(null); // Stop the mic if it never started getting data
            isRecording = false;
            yield break; // Exit coroutine
        }
        else
        {
            Debug.Log("✅ Micrófono detectó datos. Posición inicial: " + Microphone.GetPosition(null));
        }
    }

    // Coroutine to continuously monitor and update the maximum position reached by the microphone's write head.
    // This helps in diagnostics and as a fallback, but the final length is precisely calculated.
    IEnumerator MonitorMicrophonePosition()
    {
        while (isRecording) // Monitor as long as recording is active
        {
            int currentPosition = Microphone.GetPosition(null);
            
            // Only update if the position is increasing or is a valid reading (not 0 or negative).
            if (currentPosition > maxRecordedPosition)
            {
                maxRecordedPosition = currentPosition;
            }
            yield return null; // Wait for the next frame before checking again
        }
    }

    // This method stops the recording and saves the audio.
    // It now extracts all data *before* stopping the microphone.
    public void StopRecordingAndSave() 
    {
        if (!isRecording) return; // If not recording, do nothing

        // --- CRITICAL FIX: Extract ALL data from recordedClip BEFORE Microphone.End() ---
        // This ensures we get all samples before the buffer might be cleared.
        float[] allRecordedSamplesFromBuffer = new float[recordedClip.samples * recordedClip.channels];
        recordedClip.GetData(allRecordedSamplesFromBuffer, 0); 
        Debug.Log($"Debug: Extracted {allRecordedSamplesFromBuffer.Length} samples from initial recordedClip buffer.");

        // Stop the microphone recording. This will stop input and set Microphone.GetPosition(null) to 0.
        Microphone.End(null);
        isRecording = false; // Set recording status to false

        Debug.Log("🛑 Grabación finalizada. Guardando archivo...");

        if (recordedClip == null) // This check might be less relevant now, but keep for safety.
        {
            Debug.LogError("❌ Error: El AudioClip grabado es nulo después de detener el micrófono.");
            return;
        }

        // Use the maximum position we tracked during the recording as the "end point" of data
        // for the circular buffer logic.
        int finalPositionFromMonitor = maxRecordedPosition;


        // Calculate the EXACT number of samples we EXPECT for the desired recording duration (e.g., 5 seconds)
        // We trust our fixed 5-second wait. This ensures the WAV file duration matches the recording time.
        int desiredRecordingDurationSeconds = 5;
        // The precise total number of samples is (frequency * duration * channels).
        // Use the ACTUAL properties of the recordedClip, which Unity adapted to the mic.
        int preciseTotalSamples = recordedClip.frequency * desiredRecordingDurationSeconds * recordedClip.channels;

        Debug.Log($"Debug: Desired recording duration (fixed): {desiredRecordingDurationSeconds} seconds.");
        Debug.Log($"Debug: Actual AudioClip Freq: {recordedClip.frequency}Hz, Channels: {recordedClip.channels}");
        Debug.Log($"Debug: Precise total samples for {desiredRecordingDurationSeconds}s: {preciseTotalSamples}");
        Debug.Log($"Debug: Max Position tracked by Monitor: {finalPositionFromMonitor}"); // Still useful for diagnostics

        // --- Sanity Check: Compare tracked position with calculated expected ---
        if (finalPositionFromMonitor < preciseTotalSamples * 0.9f) // If tracked position is less than 90% of expected
        {
            Debug.LogWarning($"⚠️ Warning: Monitor tracked ({finalPositionFromMonitor}) is less than expected ({preciseTotalSamples}). Recording might be shorter than {desiredRecordingDurationSeconds}s. Proceeding with precise duration.");
        }
        else if (finalPositionFromMonitor > preciseTotalSamples * 1.1f && finalPositionFromMonitor < allRecordedSamplesFromBuffer.Length + (recordedClip.samples * recordedClip.channels * 0.1f)) // If it tracked more than expected, but within reasonable limits (e.g., 10% more than 10s buffer)
        {
             Debug.LogWarning($"⚠️ Warning: Monitor tracked ({finalPositionFromMonitor}) is more than expected ({preciseTotalSamples}). This might indicate buffer wrap. Using precise duration.");
        }
        else if (finalPositionFromMonitor == 0 && preciseTotalSamples > 0)
        {
            Debug.LogWarning("⚠️ Warning: Monitor tracked 0 samples, but expected a positive count. This indicates a problem with data accumulation.");
        }


        if (preciseTotalSamples <= 0)
        {
            Debug.LogError("❌ Error: Samples calculados para grabación son inválidos (<= 0). No se puede guardar.");
            return;
        }
        
        // --- ADVANCED FIX: Handle potential circular buffer wrap-around on the extracted data ---
        // This logic is crucial if the microphone's internal buffer wrapped around.
        // The start position will be the "oldest" data that is still part of our desired 5 seconds.
        int startReadPos = (finalPositionFromMonitor - preciseTotalSamples);
        // Adjust for buffer size. Modulo ensures it's within the actual buffer length.
        startReadPos = (startReadPos % allRecordedSamplesFromBuffer.Length + allRecordedSamplesFromBuffer.Length) % allRecordedSamplesFromBuffer.Length;
        // Ensure startReadPos is not negative (modulo behavior with negative numbers can be tricky in C#)
        if (startReadPos < 0) startReadPos = 0; // Fallback, should not happen with the modulo fix.


        float[] finalTrimmedSamples = new float[preciseTotalSamples];

        Debug.Log($"Debug: Extracting {preciseTotalSamples} samples from captured buffer starting at position {startReadPos}");

        // If the data wraps around, we need to copy in two parts:
        // 1. From startReadPos to the end of the buffer.
        // 2. From the beginning of the buffer for the remaining part.
        if (startReadPos + preciseTotalSamples > allRecordedSamplesFromBuffer.Length)
        {
            int firstPartLength = allRecordedSamplesFromBuffer.Length - startReadPos;
            Array.Copy(allRecordedSamplesFromBuffer, startReadPos, finalTrimmedSamples, 0, firstPartLength);

            int secondPartLength = preciseTotalSamples - firstPartLength;
            Array.Copy(allRecordedSamplesFromBuffer, 0, finalTrimmedSamples, firstPartLength, secondPartLength);
            Debug.Log($"Debug: Copied {firstPartLength} + {secondPartLength} samples (wrapped).");
        }
        else // No wrap-around within the 5-second segment, or buffer not fully filled.
        {
            Array.Copy(allRecordedSamplesFromBuffer, startReadPos, finalTrimmedSamples, 0, preciseTotalSamples);
            Debug.Log($"Debug: Copied {preciseTotalSamples} samples (no wrap).");
        }
        // --- END ADVANCED FIX ---


        // Create the final AudioClip.
        // The 'lengthSamples' parameter for AudioClip.Create is the total number of samples *per channel*.
        AudioClip trimmedClip = AudioClip.Create(
            "TrimmedClip", 
            preciseTotalSamples / recordedClip.channels, // Samples *per channel*
            recordedClip.channels,                       // Actual number of channels
            recordedClip.frequency,                      // Actual frequency (e.g., 16000Hz)
            false                                        // Not a streaming clip, all data in memory
        );
        
        // Set the audio data to the trimmed clip.
        trimmedClip.SetData(finalTrimmedSamples, 0);

        // Save the precisely trimmed audio clip to a WAV file using WavUtility.
        SaveAsWav(trimmedClip, fileName);
    }

    // Helper method to save the AudioClip to a WAV file using the WavUtility class.
    private void SaveAsWav(AudioClip clip, string filename)
    {
        try
        {
            if (clip == null)
            {
                Debug.LogError("❌ AudioClip es null. No se puede guardar.");
                return;
            }

            string filepath = Path.Combine(Application.persistentDataPath, filename);
            Debug.Log("💾 Guardando archivo en: " + filepath);

            // Call the static WavUtility method to convert the AudioClip to WAV bytes.
            // Ensure your WavUtility.cs file contains the latest code provided below.
            byte[] wavData = WavUtility.FromAudioClip(clip, out _); 
            File.WriteAllBytes(filepath, wavData); // Write the bytes to the file

            Debug.Log("✅ Archivo guardado correctamente.");
        }
        catch (System.Exception e)
        {
            Debug.LogError("❌ Error al guardar WAV: " + e.Message);
        }
    }
}