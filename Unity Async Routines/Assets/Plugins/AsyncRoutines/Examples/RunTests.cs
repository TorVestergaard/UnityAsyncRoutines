using UnityEngine;
using UnityEngine.UI;

public class RunTests : MonoBehaviour {

	public Text statusText;

	public void BackgroundTests() {
		statusText.text = "Tests Started.";
		gameObject.AddComponent<AsyncTests>();
	}
}
