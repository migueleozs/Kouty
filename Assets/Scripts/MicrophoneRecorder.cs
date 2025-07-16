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

    // We'll use this to store the highest position reached during recording
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
            yield break; // Exit the coroutine
        }

        Debug.Log("🎙️ Datos de micrófono detectados. Esperando la duración principal...");
        
        // Reset maxRecordedPosition for this new recording
        maxRecordedPosition = 0;

        // Start a sub-coroutine to continuously monitor the microphone position
        // and update maxRecordedPosition
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
            yield break; // Exit if no mic
        }

        // You can specify a device if you have multiple, like Microphone.devices[0]
        string micDevice = null; // Use default microphone
        if (Microphone.devices.Length > 0)
        {
            micDevice = Microphone.devices[0]; // Or choose a specific one from Microphone.devices
            Debug.Log($"🎙 Using microphone device: {micDevice}");
        }

        Debug.Log("🎙 Empezando grabación y esperando datos...");

        // Get microphone device capabilities
        int minFreq, maxFreq;
        Microphone.GetDeviceCaps(micDevice, out minFreq, out maxFreq);
        Debug.Log($"🎙 Microphone device capabilities: MinFreq={minFreq}Hz, MaxFreq={maxFreq}Hz");

        // Determine desired recording frequency.
        // It's best to use a standard frequency like 44100 or 48000 if supported.
        int desiredFrequency = 44100; // Common audio CD quality
        if (desiredFrequency < minFreq || desiredFrequency > maxFreq)
        {
            Debug.LogWarning($"🎙 Desired frequency {desiredFrequency}Hz is outside device capabilities [{minFreq}-{maxFreq}]Hz. Using {maxFreq}Hz instead.");
            desiredFrequency = maxFreq; // Fallback to max supported if desired is out of range
        }

        // Start recording with a sufficient buffer (e.g., 10 seconds capacity for a 5-second recording).
        // The 'false' means it's not a loop.
        recordedClip = Microphone.Start(micDevice, false, 10, desiredFrequency); 
        isRecording = true;

        // IMPORTANT: Log the actual properties of the AudioClip Unity created for the microphone.
        int actualChannels = recordedClip.channels;
        int actualFrequency = recordedClip.frequency;
        Debug.Log($"🎙 AudioClip created with Frequency: {actualFrequency}Hz, Channels: {actualChannels}");

        int attempts = 0;
        const int maxAttempts = 200; // Try for up to 200 frames (approx 3-4 seconds at 60fps)

        // Wait until Microphone.GetPosition returns a positive value (meaning data is flowing)
        // or until maxAttempts is reached (to prevent infinite loop if no mic input)
        while (Microphone.GetPosition(null) <= 0 && attempts < maxAttempts)
        {
            attempts++;
            yield return null; // Wait for the next frame
        }

        if (Microphone.GetPosition(null) <= 0)
        {
            Debug.LogError("❌ Error: Micrófono no detectó audio después de multiple intentos.");
            Microphone.End(null); // Stop the mic if it never started getting data
            isRecording = false;
        }
        else
        {
            Debug.Log("✅ Micrófono detectó datos. Posición inicial: " + Microphone.GetPosition(null));
        }
    }

    // New Coroutine to continuously monitor and update the maximum position
    IEnumerator MonitorMicrophonePosition()
    {
        while (isRecording) // Monitor as long as recording is active
        {
            int currentPosition = Microphone.GetPosition(null);
            
            // Only update if the position is increasing or is a valid reading.
            // If currentPosition becomes 0 unexpectedly while isRecording is true,
            // it means the buffer might have wrapped or there's an issue.
            // We're interested in the *peak* position before the stop.
            if (currentPosition > maxRecordedPosition)
            {
                maxRecordedPosition = currentPosition;
            }
            // You could add more complex logic here to handle buffer wrap-around for very long recordings,
            // but for a fixed 5s recording into a 10s buffer, simple max tracking is usually sufficient.
            
            yield return null; // Wait for the next frame
        }
    }

    // Modified StopRecordingAndSave to accept the final position as an argument
    public void StopRecordingAndSave(int finalRecordedPosition)
    {
        if (!isRecording) return;

        // Stop the microphone recording. This will stop input and set Microphone.GetPosition(null) to 0.
        Microphone.End(null);
        isRecording = false;

        Debug.Log("🛑 Grabación finalizada. Guardando archivo...");

        if (recordedClip == null)
        {
            Debug.LogError("❌ Error: El AudioClip grabado es nulo.");
            return;
        }

        // Use the finalRecordedPosition passed in, which was tracked continuously
        Debug.Log($"Debug: Final Recorded Position from Monitor: {finalRecordedPosition}");

        // Add a sanity check for the recorded length
        float expectedSamplesForRecordingDuration = recordedClip.frequency * 5f; // 5f for 5 seconds
        Debug.Log($"Debug: Expected samples for 5s recording: {expectedSamplesForRecordingDuration}");
        if (Mathf.Abs(finalRecordedPosition - expectedSamplesForRecordingDuration) > (recordedClip.frequency * 0.5f)) // Allow 0.5s deviation
        {
            Debug.LogWarning($"⚠️ Warning: Actual recorded samples ({finalRecordedPosition}) significantly different from expected 5s ({expectedSamplesForRecordingDuration}). This might indicate a problem with timing or buffer management.");
        }


        if (finalRecordedPosition <= 0)
        {
            Debug.LogError("❌ Error: Posición final de grabación inválida. No se grabó audio válido.");
            return;
        }
        
        // --- CRITICAL PART FOR AUDIO LENGTH ---
        // Create a new float array to hold *only* the actually recorded samples.
        // Its size is determined by the max position tracked and the number of channels.
        float[] samples = new float[finalRecordedPosition * recordedClip.channels];
        
        // Copy the recorded data from the start of the larger 'recordedClip' buffer
        // into our new, correctly sized 'samples' array.
        // The '0' indicates to start copying from the beginning of the recordedClip.
        recordedClip.GetData(samples, 0); 

        // Create a new AudioClip specifically sized to the actual recorded length.
        // Its 'samples' property will be 'finalRecordedPosition'.
        // It's crucial to use recordedClip.channels and recordedClip.frequency for consistency.
        AudioClip trimmedClip = AudioClip.Create(
            "TrimmedClip", 
            finalRecordedPosition, 
            recordedClip.channels,   // Use the actual channels the mic recorded
            recordedClip.frequency,  // Use the actual frequency the mic recorded
            false                    // Not a streaming clip
        );
        
        // Set the data of the trimmedClip using the 'samples' array we just populated.
        trimmedClip.SetData(samples, 0);

        // Save the precisely trimmed audio clip to a WAV file using WavUtility.
        SaveAsWav(trimmedClip, fileName);
    }

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

            // Call the WavUtility (which contains the SavWav logic) to convert the AudioClip to WAV bytes.
            // Ensure you have your WavUtility.cs file updated with the latest code provided.
            byte[] wavData = WavUtility.FromAudioClip(clip, out _); 
            File.WriteAllBytes(filepath, wavData);

            Debug.Log("✅ Archivo guardado correctamente.");
        }
        catch (System.Exception e)
        {
            Debug.LogError("❌ Error al guardar WAV: " + e.Message);
        }
    }
}