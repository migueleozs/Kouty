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
        StopRecordingAndSave(maxRecordedPosition); 
    }

    IEnumerator StartRecordingAndConfirmData()
    {
        if (Microphone.devices.Length == 0)
        {
            Debug.LogError("❌ No se detectó ningún micrófono.");
            yield break; // Exit if no microphone is detected
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
        }
        else
        {
            Debug.Log("✅ Micrófono detectó datos. Posición inicial: " + Microphone.GetPosition(null));
        }
    }

    // Coroutine to continuously monitor and update the maximum position reached by the microphone's write head.
    // This helps in diagnostics and as a fallback, but the final length is precisely calculated for saving.
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
    // It takes the maxPositionReachedByMonitor as an argument for diagnostics/fallback.
    public void StopRecordingAndSave(int maxPositionReachedByMonitor) 
    {
        if (!isRecording) return; // If not recording, do nothing

        // Stop the microphone recording. This will stop input and set Microphone.GetPosition(null) to 0.
        Microphone.End(null);
        isRecording = false; // Set recording status to false

        Debug.Log("🛑 Grabación finalizada. Guardando archivo...");

        if (recordedClip == null)
        {
            Debug.LogError("❌ Error: El AudioClip grabado es nulo.");
            return;
        }

        // --- CRITICAL FIX: Calculate the EXACT number of samples for the desired duration ---
        // We trust our fixed 5-second wait. This ensures the WAV file duration matches the recording time.
        int desiredRecordingDurationSeconds = 5;
        // The precise total number of samples is (frequency * duration * channels).
        // Use the ACTUAL properties of the recordedClip, which Unity adapted to the mic.
        int preciseTotalSamples = recordedClip.frequency * desiredRecordingDurationSeconds * recordedClip.channels;

        Debug.Log($"Debug: Desired recording duration (fixed): {desiredRecordingDurationSeconds} seconds.");
        Debug.Log($"Debug: Actual AudioClip Freq: {recordedClip.frequency}Hz, Channels: {recordedClip.channels}");
        Debug.Log($"Debug: Precise total samples for {desiredRecordingDurationSeconds}s: {preciseTotalSamples}");
        Debug.Log($"Debug: Max Position tracked by Monitor: {maxPositionReachedByMonitor}"); // Still useful for diagnostics

        // --- Sanity Check: Compare tracked position with calculated expected ---
        // This warning helps understand if the microphone truly recorded less data than expected for 5 seconds.
        if (maxPositionReachedByMonitor < preciseTotalSamples * 0.9f) // If tracked position is less than 90% of expected
        {
            Debug.LogWarning($"⚠️ Warning: Monitor tracked ({maxPositionReachedByMonitor}) is less than expected ({preciseTotalSamples}). Recording might be shorter than {desiredRecordingDurationSeconds}s. Proceeding with precise duration.");
        }
        else if (maxPositionReachedByMonitor > preciseTotalSamples * 1.1f) // If tracked position is more than 110% of expected
        {
             Debug.LogWarning($"⚠️ Warning: Monitor tracked ({maxPositionReachedByMonitor}) is much more than expected ({preciseTotalSamples}). This might indicate a buffer issue or misreporting. Using precise duration.");
        }

        if (preciseTotalSamples <= 0)
        {
            Debug.LogError("❌ Error: Samples calculados para grabación son inválidos (<= 0). No se puede guardar.");
            return;
        }
        
        // Create a new float array with the PRECISELY calculated number of samples.
        // This array will hold the actual audio data for the final WAV file.
        float[] samples = new float[preciseTotalSamples]; 
        
        // Copy data from the 'recordedClip' (the larger 10-second buffer) into our new,
        // precisely sized 'samples' array. This extracts only the data for the 5-second duration.
        recordedClip.GetData(samples, 0); 

        // Create the final AudioClip that will be saved.
        // The 'lengthSamples' parameter for AudioClip.Create is the total number of samples *per channel*.
        // So, we divide preciseTotalSamples (which is total samples across all channels) by recordedClip.channels.
        AudioClip trimmedClip = AudioClip.Create(
            "TrimmedClip", 
            preciseTotalSamples / recordedClip.channels, // Samples *per channel* (e.g., 80000 for mono 5s)
            recordedClip.channels,                       // Actual number of channels (e.g., 1)
            recordedClip.frequency,                      // Actual frequency (e.g., 16000Hz)
            false                                        // Not a streaming clip, all data in memory
        );
        
        // Set the audio data to the trimmed clip. This populates the AudioClip.
        trimmedClip.SetData(samples, 0);

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