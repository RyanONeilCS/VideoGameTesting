using System;
using System.Threading.Tasks;
using Unity.Services.Vivox;
using UnityEngine;

/// <summary>
/// PROXIMITY VOICE CHAT (Vivox): talk to your teammates, but only the ones NEAR you can hear you, quieter the
/// further away they are (like real life). Everyone in the match joins the same "positional" voice channel, and
/// this script keeps telling Vivox where YOUR head is, so it can work out who hears whom.
///
/// Added to YOUR player only, in Host/Join matches (not Solo). V = mute / unmute your microphone.
/// Needs: the project linked to Unity Cloud, and Vivox switched on for it in the Unity Cloud dashboard.
///
/// (Note: walls don't block voice, only distance does. Monsters don't hear your voice... yet.)
/// </summary>
public class ProximityVoice : MonoBehaviour
{
    [Tooltip("Beyond this many metres you can't hear someone at all.")]
    public int audibleDistance = 30;
    [Tooltip("Within this many metres someone sounds at full volume.")]
    public int fullVolumeDistance = 3;

    /// <summary>A short line for the HUD: connected? muted?</summary>
    public static string HudText
    {
        get
        {
            if (channel == null) return status;
            return VivoxService.Instance.IsInputDeviceMuted ? "VOICE: MUTED  [V] unmute" : "VOICE: mic on  [V] mute";
        }
    }

    static string channel;          // the voice channel we're in (null = not in one)
    static string status = "";
    static bool vivoxReady;         // Vivox started (stays started between matches)

    Transform head;
    float nextPositionUpdate;

    void Start()
    {
        head = GetComponent<PlayerAIState>().playerCamera.transform;
        _ = JoinWhenReady();
    }

    async Task JoinWhenReady()
    {
        try
        {
            status = "VOICE: connecting...";

            // The host picks the channel name (NetGame) so everyone in this match ends up in the same channel
            while (NetGame.Instance == null || NetGame.Instance.VoiceChannel == 0)
            {
                await Task.Yield();
                if (this == null) return;
            }
            string name = "stalker_" + NetGame.Instance.VoiceChannel;

            await OnlineServices.SignInAsync();
            if (!vivoxReady)
            {
                await VivoxService.Instance.InitializeAsync();
                vivoxReady = true;
            }
            if (!VivoxService.Instance.IsLoggedIn) await VivoxService.Instance.LoginAsync(new LoginOptions());

            var properties = new Channel3DProperties(audibleDistance, fullVolumeDistance, 1f, AudioFadeModel.InverseByDistance);
            await VivoxService.Instance.JoinPositionalChannelAsync(name, ChatCapability.AudioOnly, properties);
            if (this == null) { _ = VivoxService.Instance.LeaveChannelAsync(name); return; }   // left the match meanwhile
            channel = name;
        }
        catch (Exception e)
        {
            status = "VOICE: unavailable (see Console)";
            Debug.LogWarning("Voice chat couldn't start. Is the project linked to Unity Cloud, with Vivox switched on?\n" + e);
        }
    }

    void Update()
    {
        if (channel == null) return;

        if (InputHelper.MuteTogglePressed())
        {
            if (VivoxService.Instance.IsInputDeviceMuted) VivoxService.Instance.UnmuteInputDevice();
            else VivoxService.Instance.MuteInputDevice();
        }

        // Tell Vivox where my head is (a few times a second is plenty)
        if (Time.time >= nextPositionUpdate)
        {
            nextPositionUpdate = Time.time + 0.2f;
            VivoxService.Instance.Set3DPosition(head.gameObject, channel);
        }
    }

    void OnDestroy()
    {
        if (channel == null) return;
        string leaving = channel;
        channel = null;
        status = "";
        try { _ = VivoxService.Instance.LeaveChannelAsync(leaving); }
        catch (Exception e) { Debug.LogWarning(e); }
    }
}
