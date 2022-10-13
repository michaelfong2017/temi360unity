﻿using System.Collections;
using System.Collections.Generic;
using UnityEngine;
using System;
using System.Runtime.InteropServices;
#if UNITY_ANDROID
using UnityEngine.Android;
#endif

public class SoraSample : MonoBehaviour
{
    public enum SampleType
    {
        MultiSendrecv,
        MultiRecvonly,
        MultiSendonly,
        Sendonly,
        Recvonly,
    }

    Sora sora;
    public SampleType sampleType;
    
    SampleType fixedSampleType;

    
    uint trackId = 0;
    public GameObject renderTarget;

    
    Dictionary<uint, GameObject> tracks = new Dictionary<uint, GameObject>();
    public GameObject scrollViewContent;
    public GameObject baseContent;

    
    public string signalingUrl = "";
    public string channelId = "";
    public string signalingKey = "";

    public bool captureUnityCamera;
    public Camera capturedCamera;

    public Sora.VideoCodec videoCodec = Sora.VideoCodec.VP9;

    public bool unityAudioInput = false;
    public AudioSource audioSourceInput;
    public bool unityAudioOutput = false;
    public AudioSource audioSourceOutput;

    public string videoCapturerDevice = "";
    public string audioRecordingDevice = "";
    public string audioPlayoutDevice = "";
	public bool audioOnly=false;

    public bool Recvonly { get { return fixedSampleType == SampleType.Recvonly || fixedSampleType == SampleType.MultiRecvonly; } }
    public bool MultiRecv { get { return fixedSampleType == SampleType.MultiRecvonly || fixedSampleType == SampleType.MultiSendrecv; } }
    public bool Multistream { get { return fixedSampleType == SampleType.MultiSendonly || fixedSampleType == SampleType.MultiRecvonly || fixedSampleType == SampleType.MultiSendrecv; } }
    public Sora.Role Role
    {
        get
        {
            return
                fixedSampleType == SampleType.Sendonly ? Sora.Role.Sendonly :
                fixedSampleType == SampleType.Recvonly ? Sora.Role.Recvonly :
                fixedSampleType == SampleType.MultiSendonly ? Sora.Role.Sendonly :
                fixedSampleType == SampleType.MultiRecvonly ? Sora.Role.Recvonly : Sora.Role.Sendrecv;
        }
    }

    Queue<short[]> audioBuffer = new Queue<short[]>();
    int audioBufferSamples = 0;
    int audioBufferPosition = 0;

    void DumpDeviceInfo(string name, Sora.DeviceInfo[] infos)
    {
        Debug.LogFormat("------------ {0} --------------", name);
        foreach (var info in infos)
        {
            Debug.LogFormat("DeviceName={0} UniqueName={1}", info.DeviceName, info.UniqueName);
        }
    }

    // Start is called before the first frame update
    void Start()
    {

#if !UNITY_EDITOR && UNITY_ANDROID
		if (!Permission.HasUserAuthorizedPermission(Permission.Microphone)) Permission.RequestUserPermission(Permission.Microphone);
		if (!Permission.HasUserAuthorizedPermission(Permission.Camera)) Permission.RequestUserPermission(Permission.Camera);
        var x = WebCamTexture.devices;
        var y = Microphone.devices;
#endif
        fixedSampleType = sampleType;

        DumpDeviceInfo("video capturer devices", Sora.GetVideoCapturerDevices());
        DumpDeviceInfo("audio recording devices", Sora.GetAudioRecordingDevices());
        DumpDeviceInfo("audio playout devices", Sora.GetAudioPlayoutDevices());

        if (!MultiRecv)
        {
            var image = renderTarget.GetComponent<UnityEngine.UI.RawImage>();
            image.texture = new Texture2D(640, 480, TextureFormat.RGBA32, false);
        }
        StartCoroutine(Render());
        StartCoroutine(GetStats());
    }

    IEnumerator Render()
    {
        while (true)
        {
            yield return new WaitForEndOfFrame();
            if (sora != null)
            {
                sora.OnRender();
            }
            if (sora != null && !Recvonly)
            {
                var samples = AudioRenderer.GetSampleCountForCaptureFrame();
                if (AudioSettings.speakerMode == AudioSpeakerMode.Stereo)
                {
                    using (var buf = new Unity.Collections.NativeArray<float>(samples * 2, Unity.Collections.Allocator.Temp))
                    {
                        AudioRenderer.Render(buf);
                        sora.ProcessAudio(buf.ToArray(), 0, samples);
                    }
                }
            }
        }
    }
    IEnumerator GetStats()
    {
        while (true)
        {
            yield return new WaitForSeconds(5);
            if (sora == null)
            {
                continue;
            }
			//sora.SendDataChannelMessage("testmessage");//Call this anywhere else to send datachannel messages. This is here to send test messages to ensure native library and unity application comm successful.
            sora.GetStats((stats) =>
            {
                Debug.LogFormat("GetStats: {0}", stats);
            });
        }
    }
	/*
	Call sora.SendDataChannelMessage(String here); to send datachannel messages. Calling this method with string is enough to send data_channel messages.
	
	*/
    // Update is called once per frame
    void Update()
    {
        if (sora == null)
        {
            return;
        }

        sora.DispatchEvents();

        if (!MultiRecv)
        {
            if (trackId != 0)
            {
                var image = renderTarget.GetComponent<UnityEngine.UI.RawImage>();
                sora.RenderTrackToTexture(trackId, image.texture);
            }
        }
        else
        {
            foreach (var track in tracks)
            {
                var image = track.Value.GetComponent<UnityEngine.UI.RawImage>();
                sora.RenderTrackToTexture(track.Key, image.texture);
            }
        }
    }
    void InitSora()
    {
        DisposeSora();

        sora = new Sora();
        if (!MultiRecv)
        {
            sora.OnAddTrack = (trackId) =>
            {
                Debug.LogFormat("OnAddTrack: trackId={0}", trackId);
                this.trackId = trackId;
            };
            sora.OnRemoveTrack = (trackId) =>
            {
                Debug.LogFormat("OnRemoveTrack: trackId={0}", trackId);
                this.trackId = 0;
            };
        }
        else
        {
            sora.OnAddTrack = (trackId) =>
            {
                Debug.LogFormat("OnAddTrack: trackId={0}", trackId);
                var obj = GameObject.Instantiate(baseContent, Vector3.zero, Quaternion.identity);
                obj.name = string.Format("track {0}", trackId);
                obj.transform.SetParent(scrollViewContent.transform);
                obj.SetActive(true);
                var image = obj.GetComponent<UnityEngine.UI.RawImage>();
                image.texture = new Texture2D(320, 240, TextureFormat.RGBA32, false);
                tracks.Add(trackId, obj);
            };
            sora.OnRemoveTrack = (trackId) =>
            {
                Debug.LogFormat("OnRemoveTrack: trackId={0}", trackId);
                if (tracks.ContainsKey(trackId))
                {
                    GameObject.Destroy(tracks[trackId]);
                    tracks.Remove(trackId);
                }
            };
        }
        sora.OnNotify = (json) => //Json is the variable that messages are taken through datachannel. So, when a user messages you, onNotify will be called. So you can get messages here.
        {
            Debug.LogFormat("OnNotify: {0}", json);
        };
        
        sora.OnHandleAudio = (buf, samples, channels) =>
        {
            lock (audioBuffer)
            {
                audioBuffer.Enqueue(buf);
                audioBufferSamples += samples;
            }
        };

        if (unityAudioOutput)
        {
            var audioClip = AudioClip.Create("AudioClip", 480000, 1, 48000, true, (data) =>
            {
                lock (audioBuffer)
                {
                    if (audioBufferSamples < data.Length)
                    {
                        for (int i = 0; i < data.Length; i++)
                        {
                            data[i] = 0.0f;
                        }
                        return;
                    }

                    var p = audioBuffer.Peek();
                    for (int i = 0; i < data.Length; i++)
                    {
                        data[i] = p[audioBufferPosition] / 32768.0f;
                        ++audioBufferPosition;
                        if (audioBufferPosition >= p.Length)
                        {
                            audioBuffer.Dequeue();
                            p = audioBuffer.Peek();
                            audioBufferPosition = 0;
                        }
                    }
                    audioBufferSamples -= data.Length;
                }
            });
            audioSourceOutput.clip = audioClip;
            audioSourceOutput.Play();
        }

        if (!Recvonly)
        {
            AudioRenderer.Start();
            audioSourceInput.Play();
        }
    }
    void DisposeSora()
    {
        if (sora != null)
        {
            sora.Dispose();
            sora = null;
            Debug.Log("Sora is Disposed");
            if (MultiRecv)
            {
                foreach (var track in tracks)
                {
                    GameObject.Destroy(track.Value);
                }
                tracks.Clear();
            }
            if (!Recvonly)
            {
                audioSourceInput.Stop();
                AudioRenderer.Stop();
            }

            if (unityAudioOutput)
            {
                audioSourceOutput.Stop();
            }
        }
    }

    [Serializable]
    class Settings
    {
        public string signaling_url = "";
        public string channel_id = "";
        public string signaling_key = "";
    }

    [Serializable]
    class Metadata
    {
        public string signaling_key;
    }

    public void OnClickStart()
    {

        if (signalingUrl.Length == 0 && channelId.Length == 0 && System.IO.File.Exists(".env.json"))
        {
            var settings = JsonUtility.FromJson<Settings>(System.IO.File.ReadAllText(".env.json"));
            signalingUrl = settings.signaling_url;
            channelId = settings.channel_id;
            signalingKey = settings.signaling_key;
        }

        if (signalingUrl.Length == 0)
        {
            Debug.LogError("Signaling URL is not set");
            return;
        }
        if (channelId.Length == 0)
        {
            Debug.LogError("Channel ID is not set.");
            return;
        }
        // signalingKey 
        string metadata = "";
        if (signalingKey.Length != 0)
        {
            var md = new Metadata()
            {
                signaling_key = signalingKey
            };
            metadata = JsonUtility.ToJson(md);
        }

        InitSora();

        var config = new Sora.Config()
        {
            SignalingUrl = signalingUrl,
            ChannelId = channelId,
            Metadata = metadata,
            Role = Role,
            Multistream = Multistream,
            VideoCodec = videoCodec,
            UnityAudioInput = unityAudioInput,
            UnityAudioOutput = unityAudioOutput,
            VideoCapturerDevice = videoCapturerDevice,
            AudioRecordingDevice = audioRecordingDevice,
            AudioPlayoutDevice = audioPlayoutDevice,
			AudioOnly=audioOnly,
        };
        if (captureUnityCamera && capturedCamera != null || Sora.GetVideoCapturerDevices().Length==0)
        {
            if (capturedCamera == null)
                capturedCamera = UnityEngine.Camera.main;
			Debug.LogFormat(capturedCamera.name);
            config.CapturerType = Sora.CapturerType.UnityCamera;
            config.UnityCamera = capturedCamera;
        }

        var success = sora.Connect(config);
        if (!success)
        {
            sora.Dispose();
            sora = null;
            Debug.LogErrorFormat("Sora.Connect failed: signalingUrl={0}, channelId={1}", signalingUrl, channelId);
            return;
        }
        Debug.LogFormat("Sora is Created: signalingUrl={0}, channelId={1}", signalingUrl, channelId);
    }
    public void OnClickEnd()
    {
        DisposeSora();
    }

    void OnApplicationQuit()
    {
        DisposeSora();
    }
}