using System.Collections;
using UnityEngine;
using System.Threading;
using UnityEngine.UI;

public class FadeColor : MonoBehaviour
{

    public Transform cube;
    public Color defaultColor = Color.white;
    public Text statusText;
    private Renderer rend;

    void Awake()
    {
        rend = cube.GetComponent<Renderer>();
        // need to make sure the renderer is set to fade
        rend.material.SetFloat("_Mode", 2);
        // need to have the unity editor load the new mode to make it active apparently
        HackyEditorCrap();
    }
    public void FadeCubeSync()
    {
        // Note: this will not appear to fade the color becase it's not letting the next frame update
        // You would usually solve this with Co-Routines - see next method.
        statusText.text = "Blocking started Fading....";
        rend.material.color = defaultColor;
        for (float f = 1f; f >= 0; f -= 0.05f)
        {
            Color c = rend.material.color;
            c.a = f;
            rend.material.color = c;
            Thread.Sleep(100);
        }
        Thread.Sleep(2000);
        rend.material.color = defaultColor;
        Debug.Log("Blocking finished fading color");
        statusText.text = "Blocking finished fading...";
    }

    public void FadeCubeCoRo()
    {
        StartCoroutine("FadeCoRo");
    }

    public void FadeCubeAsync()
    {
        Async.Run(Fade);
    }

    IEnumerator Fade()
    {
        yield return Async.ToGame;  // we need to interact with game objects, switch to game thread
        statusText.text = "Async fading started";
        rend.material.color = defaultColor;
        yield return Async.ToAsync; // we are doing non-game stuff, switch out of main thread 
        for (float f = 1f; f >= 0; f -= 0.05f)
        {
            yield return Async.ToGame; // need to do game stuff, switching to game thread
            Color c = rend.material.color;
            c.a = f;
            rend.material.color = c;
            yield return Async.ToAsync; // switch out of game thread
            Thread.Sleep(100);
        }
        Thread.Sleep(2000);
        yield return Async.ToGame; // switch to game thread to set the color back to default
        rend.material.color = defaultColor;
        yield return null; // stop
        statusText.text = "Async fading finished";
        Debug.Log("Async finished fading color");
    }

    // Note how similar the coroutine is - however Async has more functionality
    IEnumerator FadeCoRo()
    {
        statusText.text = "Coroutine fading started";
        rend.material.color = defaultColor;
        for (float f = 1f; f >= 0; f -= 0.05f)
        {
            Color c = rend.material.color;
            c.a = f;
            rend.material.color = c;
            yield return new WaitForSeconds(.1f); 
            yield return null;
        }
        yield return new WaitForSeconds(2f);
        yield return null;
        rend.material.color = defaultColor;
        statusText.text = "Coroutine fading ended";
        Debug.Log("Co-Routine finished fading color");
    }

    // Note: Without this you have to open the default material to trigger this same 
    // code in order for the mode rendering to change to 'Fade'
    // I didn't want to include any materials and bloat the examples 
    // and you can't edit the default material when it's not running for obvious reasons.
    void HackyEditorCrap()
    {
        rend.material.SetOverrideTag("RenderType", "Transparent");
        rend.material.SetInt("_SrcBlend", 5);
        rend.material.SetInt("_DstBlend", 10);
        rend.material.SetInt("_ZWrite", 0);
        rend.material.DisableKeyword("_ALPHATEST_ON");
        rend.material.EnableKeyword("_ALPHABLEND_ON");
        rend.material.EnableKeyword("_ALPHAPREMULTIPLY_ON");
        rend.material.renderQueue = 3000;
    }
}
