using UnityEngine;
using System.Text;
using System.Collections;
using UnityEngine.Networking;
using UnityEngine.Windows.Speech;

public class VoiceManager : MonoBehaviour
{
    private DictationRecognizer recognizer;

    void Awake()
    {
        recognizer = new DictationRecognizer();
        recognizer.DictationResult += OnDictationResult;
    }

    public void StartListening()
    {
        Debug.Log("🎙️ Iniciando reconocimiento de voz...");
        recognizer.Start();
    }

    private void OnDictationResult(string text, ConfidenceLevel confidence)
    {
        Debug.Log("🗣️ Usuario dijo: " + text);
        recognizer.Stop(); // Opcional, para evitar múltiples llamadas
        StartCoroutine(SendTextToAgent(text));
    }

    IEnumerator SendTextToAgent(string userText)
    {
        Debug.Log("💬 Enviando texto al agente...");
        // Aquí podrías usar ChatGPT o tu propio backend.
        string respuesta = "Bonjour! Je suis Kouty."; // Respuesta fija de prueba

        yield return StartCoroutine(TextToSpeech(respuesta));
    }

    IEnumerator TextToSpeech(string text)
    {
        Debug.Log("🔊 Enviando texto a ElevenLabs...");
        string apiKey = "sk_9331393b93c4cf838ed769455f7fa7254d32978c5aa79c1b";
        string voiceId = "EXAVITQu4vr4xnSDxMaL"; // Bella

        string url = $"https://api.elevenlabs.io/v1/text-to-speech/{voiceId}";
        string jsonBody = "{\"text\":\"" + text + "\",\"model_id\":\"eleven_monolingual_v1\"}";

        var request = new UnityWebRequest(url, "POST");
        byte[] bodyRaw = Encoding.UTF8.GetBytes(jsonBody);
        request.uploadHandler = new UploadHandlerRaw(bodyRaw);
        request.downloadHandler = new DownloadHandlerBuffer();
        request.SetRequestHeader("xi-api-key", apiKey);
        request.SetRequestHeader("Content-Type", "application/json");

        yield return request.SendWebRequest();

        if (request.result == UnityWebRequest.Result.Success)
        {
            Debug.Log("✅ Respuesta de ElevenLabs recibida.");
            byte[] audioData = request.downloadHandler.data;
            PlayAudioFromBytes(audioData);
        }
        else
        {
            Debug.LogError("❌ Error en ElevenLabs: " + request.error);
        }
    }

    void PlayAudioFromBytes(byte[] audioData)
    {
        string filePath = Application.persistentDataPath + "/response.mp3";
        System.IO.File.WriteAllBytes(filePath, audioData);
        StartCoroutine(PlayAudio(filePath));
    }

    IEnumerator PlayAudio(string filePath)
    {
        string url = "file://" + filePath;
        using (UnityWebRequest www = UnityWebRequestMultimedia.GetAudioClip(url, AudioType.MPEG))
        {
            yield return www.SendWebRequest();

            if (www.result == UnityWebRequest.Result.Success)
            {
                AudioClip clip = DownloadHandlerAudioClip.GetContent(www);
                AudioSource audioSource = GetComponent<AudioSource>();
                if (audioSource == null)
                    audioSource = gameObject.AddComponent<AudioSource>();

                audioSource.clip = clip;
                audioSource.Play();
                Debug.Log("▶️ Reproduciendo respuesta de Kouty...");
            }
            else
            {
                Debug.LogError("❌ Error al reproducir audio: " + www.error);
            }
        }
    }
}
