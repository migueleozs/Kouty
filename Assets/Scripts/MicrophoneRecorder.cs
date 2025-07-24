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

    private int maxRecordedPosition = 0; 
    private float maxRecordingTimeSeconds = 30f; 

    // --- NUEVO CAMPO: Tiempo en que inició la grabación ---
    private float recordingStartTime; 

    void Start()
    {
        // Vacío, la grabación es por botones.
    }

    public void StartRecordingButton()
    {
        if (!isRecording)
        {
            Debug.Log("Iniciando grabación con botón Start.");
            recordingStartTime = Time.time; // Registrar el tiempo de inicio.
            StartCoroutine(RecordingFlow()); 
        }
        else
        {
            Debug.LogWarning("Ya se está grabando. Ignorando el clic del botón Start.");
        }
    }

    public void StopRecordingButton()
    {
        if (isRecording)
        {
            Debug.Log("Deteniendo grabación con botón Stop.");
            StopAllCoroutines(); 
            StopRecordingAndSave(); 
        }
        else
        {
            Debug.LogWarning("No hay grabación activa para detener.");
        }
    }

    IEnumerator RecordingFlow()
    {
        yield return StartCoroutine(StartRecordingAndConfirmData());

        if (!isRecording) 
        {
            Debug.LogError("❌ Grabación no iniciada o sin datos. Saliendo del flujo de grabación.");
            yield break; 
        }

        Debug.Log("🎙️ Datos de micrófono detectados. La grabación está activa...");
        
        maxRecordedPosition = 0;
        StartCoroutine(MonitorMicrophonePosition());

        float timer = 0f;
        while (isRecording && timer < maxRecordingTimeSeconds)
        {
            timer += Time.deltaTime;
            yield return null;
        }

        if (isRecording && timer >= maxRecordingTimeSeconds)
        {
            Debug.LogWarning($"⚠️ Tiempo máximo de grabación ({maxRecordingTimeSeconds} segundos) alcanzado. Deteniendo automáticamente.");
            StopRecordingAndSave(); 
        }
        Debug.Log("RecordingFlow ha terminado su espera.");
    }

    IEnumerator StartRecordingAndConfirmData()
    {
        if (Microphone.devices.Length == 0)
        {
            Debug.LogError("❌ No se detectó ningún micrófono. Asegúrate de que uno esté conectado y habilitado.");
            yield break; 
        }

        string micDevice = Microphone.devices[0]; 
        Debug.Log($"🎙 Usando dispositivo de micrófono: {micDevice}");

        Debug.Log("🎙 Empezando grabación y esperando datos...");

        int minFreq, maxFreq;
        Microphone.GetDeviceCaps(micDevice, out minFreq, out maxFreq);
        Debug.Log($"🎙 Capacidades del dispositivo de micrófono: MinFreq={minFreq}Hz, MaxFreq={maxFreq}Hz");

        int desiredFrequency = 44100; 
        if (desiredFrequency < minFreq || desiredFrequency > maxFreq)
        {
            Debug.LogWarning($"🎙 Frecuencia deseada {desiredFrequency}Hz está fuera de las capacidades del dispositivo [{minFreq}-{maxFreq}]Hz. Usando {maxFreq}Hz en su lugar.");
            desiredFrequency = maxFreq; 
        }

        recordedClip = Microphone.Start(micDevice, false, (int)(maxRecordingTimeSeconds * 1.5f), desiredFrequency);
        isRecording = true;

        int actualChannels = recordedClip.channels;
        int actualFrequency = recordedClip.frequency;
        Debug.Log($"🎙 AudioClip creado con Frecuencia: {actualFrequency}Hz, Canales: {actualChannels}");

        int attempts = 0;
        const int maxAttempts = 200; 

        while (Microphone.GetPosition(null) <= 0 && attempts < maxAttempts)
        {
            attempts++;
            yield return null; 
        }

        if (Microphone.GetPosition(null) <= 0)
        {
            Debug.LogError("❌ Micrófono no detectó audio después de múltiples intentos. Deteniendo grabación.");
            Microphone.End(null); 
            isRecording = false;
            yield break; 
        }
        else
        {
            Debug.Log("✅ Micrófono detectó datos. Posición inicial: " + Microphone.GetPosition(null));
        }
    }

    IEnumerator MonitorMicrophonePosition()
    {
        while (isRecording) 
        {
            int currentPosition = Microphone.GetPosition(null);
            
            if (currentPosition > maxRecordedPosition)
            {
                maxRecordedPosition = currentPosition;
            }
            yield return null; 
        }
    }

    public void StopRecordingAndSave() 
    {
        if (!isRecording) return; 

        // --- CALCULAR LA DURACIÓN REAL GRABADA EN SEGUNDOS ---
        float recordedDuration = Time.time - recordingStartTime;
        // Limitar la duración a la capacidad del buffer si se grabó más del buffer completo
        // y para evitar duraciones negativas o excesivas si algo sale mal.
        if (recordedClip != null) {
            float maxBufferDuration = (float)recordedClip.samples * recordedClip.channels / recordedClip.frequency;
            if (recordedDuration > maxBufferDuration) {
                recordedDuration = maxBufferDuration;
            }
        }
        
        // Si por alguna razón recordedDuration es menor que 0.1s, lo forzamos a 0 para evitar errores.
        if (recordedDuration < 0.1f) {
            Debug.LogWarning("⚠️ Duración de grabación muy corta (<0.1s). Ajustando a 0 para evitar errores.");
            recordedDuration = 0f;
        }

        // --- Extraer TODO el buffer ANTES de detener el micrófono ---
        float[] allRecordedSamplesFromBuffer = new float[recordedClip.samples * recordedClip.channels];
        recordedClip.GetData(allRecordedSamplesFromBuffer, 0); 
        Debug.Log($"Debug: Extracción de {allRecordedSamplesFromBuffer.Length} samples del buffer inicial (Capacidad del buffer: {recordedClip.samples / recordedClip.frequency}s) ANTES de Microphone.End().");

        Microphone.End(null);
        isRecording = false; 

        Debug.Log("🛑 Grabación finalizada. Guardando archivo...");

        if (recordedClip == null) 
        {
            Debug.LogWarning("⚠️ Warning: recordedClip se volvió nulo después de Microphone.End(). Usando los datos extraídos.");
        }
        
        // --- CALCULAR LOS SAMPLES PRECISOS BASADOS EN EL TIEMPO REAL ---
        int preciseTotalSamplesToExtract = (int)(recordedDuration * recordedClip.frequency * recordedClip.channels);
        
        // Asegurarse de que no intentamos extraer más samples de los que hay disponibles en el buffer capturado.
        // Esto es especialmente importante si la grabación fue muy corta o hubo problemas.
        if (preciseTotalSamplesToExtract > allRecordedSamplesFromBuffer.Length) {
            Debug.LogWarning($"⚠️ Advertencia: Samples a extraer ({preciseTotalSamplesToExtract}) exceden el tamaño del buffer capturado ({allRecordedSamplesFromBuffer.Length}). Ajustando a la longitud máxima del buffer.");
            preciseTotalSamplesToExtract = allRecordedSamplesFromBuffer.Length;
        }


        if (preciseTotalSamplesToExtract <= 0)
        {
            Debug.LogError("❌ Error: Samples calculados para extracción son inválidos (<= 0). No se puede guardar el audio.");
            return;
        }

        Debug.Log($"Debug: Duración real de grabación (calculada): {recordedDuration:F2} segundos.");
        Debug.Log($"Debug: Samples totales precisos a extraer: {preciseTotalSamplesToExtract}");
        Debug.Log($"Debug: Posición máxima rastreada por Monitor (diagnóstico): {maxRecordedPosition}"); // Para comparar


        float[] finalTrimmedSamples = new float[preciseTotalSamplesToExtract];

        // --- LÓGICA DE MANEJO DE BUFFER CIRCULAR MEJORADA ---
        // El 'startReadPos' es donde el segmento de audio *útil* comienza en el buffer circular.
        // Si maxRecordedPosition es la cabeza de escritura, el inicio del segmento es
        // maxRecordedPosition - preciseTotalSamplesToExtract.
        int startReadPos = (maxRecordedPosition - preciseTotalSamplesToExtract);
        
        // Ajustar para que startReadPos siempre esté dentro del rango del buffer (0 a length-1)
        startReadPos = (startReadPos % allRecordedSamplesFromBuffer.Length + allRecordedSamplesFromBuffer.Length) % allRecordedSamplesFromBuffer.Length;
        
        // Si el valor es negativo por alguna razón después del ajuste de modulo, lo forzamos a 0.
        if (startReadPos < 0) startReadPos = 0; 
        
        Debug.Log($"Debug: Extrayendo {preciseTotalSamplesToExtract} samples del buffer capturado, comenzando en la posición {startReadPos} (basado en maxRecordedPosition)");

        // Copiar los samples, manejando si el segmento se envuelve al final del buffer.
        if (startReadPos + preciseTotalSamplesToExtract > allRecordedSamplesFromBuffer.Length)
        {
            int firstPartLength = allRecordedSamplesFromBuffer.Length - startReadPos;
            Array.Copy(allRecordedSamplesFromBuffer, startReadPos, finalTrimmedSamples, 0, firstPartLength);

            int secondPartLength = preciseTotalSamplesToExtract - firstPartLength;
            Array.Copy(allRecordedSamplesFromBuffer, 0, finalTrimmedSamples, firstPartLength, secondPartLength);
            Debug.Log($"Debug: Copiados {firstPartLength} + {secondPartLength} samples (con envoltura de buffer).");
        }
        else 
        {
            Array.Copy(allRecordedSamplesFromBuffer, startReadPos, finalTrimmedSamples, 0, preciseTotalSamplesToExtract);
            Debug.Log($"Debug: Copiados {preciseTotalSamplesToExtract} samples (sin envoltura de buffer).");
        }

        // Crear el AudioClip final con las propiedades correctas y la longitud precisa.
        AudioClip trimmedClip = AudioClip.Create(
            "TrimmedClip", 
            preciseTotalSamplesToExtract / recordedClip.channels, // Samples por canal
            recordedClip.channels,                       
            recordedClip.frequency,                      
            false                                        
        );
        
        trimmedClip.SetData(finalTrimmedSamples, 0); 

        SaveAsWav(trimmedClip, fileName); 
    }

    private void SaveAsWav(AudioClip clip, string filename)
    {
        try
        {
            if (clip == null)
            {
                Debug.LogError("❌ AudioClip es null. No se puede guardar el archivo WAV.");
                return;
            }

            string filepath = Path.Combine(Application.persistentDataPath, filename);
            Debug.Log("💾 Guardando archivo WAV en: " + filepath);

            byte[] wavData = WavUtility.FromAudioClip(clip, out _); 
            File.WriteAllBytes(filepath, wavData); 

            Debug.Log("✅ Archivo WAV guardado correctamente.");
        }
        catch (System.Exception e)
        {
            Debug.LogError("❌ Error al guardar el archivo WAV: " + e.Message);
        }
    }
}