using Meta.XR.BuildingBlocks.AIBlocks;
using System;
using System.Threading.Tasks;
using UnityEngine;
using UnityEngine.InputSystem;
using System.Linq;
using Newtonsoft.Json.Linq;

public class ConversationAI : MonoBehaviour
{
    [SerializeField] internal AIProviderBase providerAsset;

    [Header("AI Building Blocks Agents")]
    [SerializeField] private SpeechToTextAgent speechToText;
    [SerializeField] private LlmAgent llm;
    [SerializeField] private TextToSpeechAgent textToSpeech;

    [Header("Input")]
    [SerializeField] private OVRInput.Button questButton = OVRInput.Button.One;

    private bool isListening;
    private bool isBusy;
    private IChatTask _chatTask;

    private void Awake()
    {
        // STT => LLM
        speechToText.onTranscript.AddListener(OnTranscript);

        // LLM => TTS
        llm.onResponseReceived.AddListener(x => textToSpeech.SpeakText(x));
    }

    private void Update()
    {
        if (isBusy) return;

        bool pressed =
            Keyboard.current.spaceKey.wasPressedThisFrame ||
            OVRInput.GetDown(questButton);

        if (!pressed) return;

        if (!isListening)
        {
            isListening = true;
            speechToText.StartListening();
            Debug.Log("STT: listening...");
        }
        else
        {
            isListening = false;
            speechToText.StopNow();
            Debug.Log("STT: stopped.");
        }
    }

    private async void OnTranscript(string transcript)
    {
        if (string.IsNullOrWhiteSpace(transcript)) return;

        // Prevent button spam while a request is in flight.
        isBusy = true;
        isListening = false;

        try
        {
            // Send to the LLM tje transcript
            await llm.SendPromptAsync(transcript);
            
            //THIS IS A WORKAROUND!!!!
            //await AskAI(transcript);
        }
        catch (Exception e)
        {
            Debug.LogException(e);
        }
        finally
        {
            isBusy = false;
        }
    }

    private async Task AskAI(string userText)
    {
        if(_chatTask == null)
            _chatTask = providerAsset as IChatTask;

        try
        {
            var req = new ChatRequest(userText);
            var res = await _chatTask.ChatAsync(req);
            Debug.Log("RESPONSE RAW : " + res.Raw);
            var resOutput = GetAnswer(res.Raw.ToString());
            Debug.Log("RESPONSE TEXT : " + resOutput);
            textToSpeech.SpeakText(resOutput);
        }
        catch (Exception ex)
        {
            Debug.Log(ex.ToString());
        }
    }


    string GetAnswer(string json)
    {
        var j = JObject.Parse(json);

        return j["output"]
            ?.First(o => (string)o["type"] == "message")?
            ["content"]?
            .First(c => (string)c["type"] == "output_text")?
            ["text"]?
            .ToString() ?? "";
    }
}
