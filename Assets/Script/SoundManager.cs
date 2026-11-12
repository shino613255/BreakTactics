using System.Collections;
using System.Collections.Generic;
using UnityEngine;

public class SoundManager : MonoBehaviour
{
    private const int BattleBgmIndex = 3;

    public static SoundManager instance;

    private void Awake()
    {
        if (instance == null)
        {
            instance = this;
            DontDestroyOnLoad(this.gameObject); 
        }
        else
        {
            Destroy(this.gameObject); 
        }
    }

    public AudioSource AudioSourceBGM;
    public AudioClip[] AudioClipsBGM; 

    public AudioSource AudioSourceSE; 
    public AudioClip[] ButtonSE; 
    
    public void StopBGM() 
    {
        AudioSourceBGM.Stop();
    }

    public void PlayBattleBGM()
    {
        AudioSourceBGM.Stop();
        AudioSourceBGM.clip = AudioClipsBGM[BattleBgmIndex];
        AudioSourceBGM.Play();
    }

    public void PlayButtonSE(int index)
    {
        AudioSourceSE.PlayOneShot(ButtonSE[index]); 
    }
}