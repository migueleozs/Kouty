using UnityEngine;
using UnityEngine.UI;

public class VoiceButton : MonoBehaviour
{
    public Button parlerButton;
    public VoiceManager voiceManager; // ✅ Referencia directa
    
    void Start()
    {
        parlerButton.onClick.AddListener(StartConversation);
    }
    
    void StartConversation()
    {
        if (voiceManager != null)
        {
            Debug.Log("Iniciando conversación con Kouty...");
            Object.FindFirstObjectByType<VoiceManager>().StartListening();
        }
        else
        {
            Debug.LogError("VoiceManager no asignado en el inspector");
        }
    }
/*
    void StartConversation()
    {
        Debug.Log("Iniciando conversación con Kouty...");
        Object.FindFirstObjectByType<VoiceManager>().StartListening();
    }
*/
}