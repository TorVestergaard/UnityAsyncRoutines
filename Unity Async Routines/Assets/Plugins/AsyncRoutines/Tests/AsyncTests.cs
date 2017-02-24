using System.Collections;
using System.Threading;
using UnityEngine;

/// <summary>
/// This component launches a soak test case when play mode is entered,
/// that floods the async routine system with working routines in an attempt
/// to suss out bugs and issues.
/// <para />
/// This file can be deleted without any issues.
/// </summary>
public class AsyncTests : MonoBehaviour
{
    [SerializeField]
    private bool logDetails = false;

    [SerializeField]
    private int count = 500;

    private int finishedCount;

    private void Log(string message)
    {
        // This is quite spammy with a high test count
        if (this.logDetails)
        {
            Debug.Log(message);
        }
    }

    private IEnumerator Start()
    {
        Async.RenderDebugGUI = true;

        this.finishedCount = 0;

        for (int i = 1; i <= this.count; i++)
        {
            if (i % 100 == 0)
            {
                Debug.Log("Started " + i);
            }

            Async.Run(this.MegaTest);
            yield return null;
        }

        Debug.Log("Started all!");

        while (this.finishedCount != this.count)
        {
            yield return null;
        }

        Debug.Log("TESTING COMPLETE");
    }

    private IEnumerator MegaTest()
    {
        this.Log("Testing starting in correct context");
        Debug.Assert(Async.Context == ExecutionContext.Game);

        this.Log("Testing thread switching");
        yield return Async.ToAsync;
        Debug.Assert(Async.Context == ExecutionContext.Async);
        yield return Async.ToGame;
        Debug.Assert(Async.Context == ExecutionContext.Game);

        this.Log("Testing waiting individual frames");
        int waitFrames = 10;
        int frameCount = Time.frameCount + 10;

        for (int i = 0; i < waitFrames; i++)
        {
            yield return null;
        }

        if (Async.ThrottleGameLoopExecutionTime)
        {
            Debug.Assert(Time.frameCount >= frameCount); // Can be larger than frame count due to throttling being enabled
        }
        else
        {
            Debug.Assert(Time.frameCount == frameCount); // Without throttling enabled this must be precise
        }

        this.Log("Testing parallel work processing");
        // Prepare data
        int[] data = new int[1024];

        for (int i = 0; i < data.Length; i++)
        {
            data[i] = Random.Range(0, 5);
        }

        // Go async for later test
        yield return Async.ToAsync;

        // Perform "fake work" for each item in array
        yield return Async.ParallelForEach(data, (int item) =>
        {
            Thread.Sleep(item);
        });

        // We should have returned to an async context
        this.Log("Testing wait for condition proper context return");
        Debug.Assert(Async.Context == ExecutionContext.Async);

        this.Log("Test Unity's WaitForSeconds");
        yield return Async.ToGame;
        float wait = 1f;
        float time = Time.time + wait;
        yield return new WaitForSeconds(wait);
        Debug.Assert(Time.time >= time);

        this.Log("Test Unity CustomYieldInstruction");
        time = Time.realtimeSinceStartup + wait;
        yield return new WaitForSecondsRealtime(wait);
        Debug.Assert(Time.realtimeSinceStartup >= time);

        this.Log("Test waiting directly for other enumerator");
        yield return this.SomeOtherTask("Direct");

        this.Log("Test waiting indirectly for enumerator");
        yield return Async.Run(this.SomeOtherTask, "Indirect with arg");

        this.Log("Test waiting for fixed update");
        yield return new WaitForFixedUpdate();
        Debug.Assert(Time.deltaTime == Time.fixedDeltaTime);

        this.Log("Test waiting for end of frame");
        int frame = Time.frameCount;
        yield return new WaitForEndOfFrame();
        // Best test I can think of; I know it'll be delayed until some time
        // I've checked this in the log and it's good enough
        Debug.Assert(Time.frameCount == frame);

        this.finishedCount++;
    }

    private IEnumerator SomeOtherTask(string test)
    {
        yield return new Async.WaitForSeconds(0.5f);
        this.Log("Finished running task: " + test);
    }
}