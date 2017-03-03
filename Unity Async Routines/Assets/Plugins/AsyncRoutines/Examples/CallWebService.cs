using UnityEngine;
using UnityEngine.UI;
using System.Net;
using System.Threading;
using System.Collections;


public class CallWebService : MonoBehaviour {

	public Text displayText;

	public void CallIPAddressServiceSync() {
		displayText.text = "Fetching Ip address... brb.";
		// just to be extra rude
		Thread.Sleep(10000);

		// this blocks on the return of DownloadString
		displayText.text = new WebClient().DownloadString("http://api.ipify.org");
	}

	public void CallIPAddressServiceAsync() {
		var handle = Async.Run(CallIPAddressAsync());
	}

	IEnumerator CallIPAddressAsync() {

		string ip = "IP n/a";
		displayText.text = "Fetching Ip address... brb.";
		yield return Async.ToAsync; // leave the unity thread and do background processing
		// just to be extra rude
		Thread.Sleep(10000);

		// this blocks on the return of DownloadString
		ip = new WebClient().DownloadString("http://api.ipify.org");

		yield return Async.ToGame; // I've got the result, need to return to unity to set the display
		displayText.text = ip;
	}

}
