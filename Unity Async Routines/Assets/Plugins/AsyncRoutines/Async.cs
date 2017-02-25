/*
 * MIT License
 *
 * Copyright(c) 2017 Tor Esa Vestergaard
 *
 * Permission is hereby granted, free of charge, to any person obtaining a copy
 * of this software and associated documentation files (the "Software"), to deal
 * in the Software without restriction, including without limitation the rights
 * to use, copy, modify, merge, publish, distribute, sublicense, and/or sell
 * copies of the Software, and to permit persons to whom the Software is
 * furnished to do so, subject to the following conditions:
 *
 * The above copyright notice and this permission notice shall be included in all
 * copies or substantial portions of the Software.
 *
 * THE SOFTWARE IS PROVIDED "AS IS", WITHOUT WARRANTY OF ANY KIND, EXPRESS OR
 * IMPLIED, INCLUDING BUT NOT LIMITED TO THE WARRANTIES OF MERCHANTABILITY,
 * FITNESS FOR A PARTICULAR PURPOSE AND NONINFRINGEMENT. IN NO EVENT SHALL THE
 * AUTHORS OR COPYRIGHT HOLDERS BE LIABLE FOR ANY CLAIM, DAMAGES OR OTHER
 * LIABILITY, WHETHER IN AN ACTION OF CONTRACT, TORT OR OTHERWISE, ARISING FROM,
 * OUT OF OR IN CONNECTION WITH THE SOFTWARE OR THE USE OR OTHER DEALINGS IN THE
 * SOFTWARE.
 *
 *
 * ------------------------ USING UNITY ASYNC ROUTINES ------------------------
 *
 * Async routines are basically fancy coroutines, only with an expanded set of
 * possible values to yield. Most fundamentally, yielding Async.ToAsync will
 * switch the method to run on a thread pool thread, and yield Async.ToGame will
 * return your method to the game thread.
 *
 * This is very useful for the cases where you need to execute heavy work, but
 * you also interact closely with Unity. This ease of switching between threads
 * is of great assistance when creating asynchronous code.
 *
 * When you start an async routine using Async.Run, you will receive a handle.
 * This handle is a convenient way for you to talk about that instance of the
 * routine in particular. You can stop the routine with the handle, or you can
 * yield it in a regular, vanilla Unity coroutine and wait for the async routine
 * to finish.
 *
 * All of Unity's own coroutine yield instructions are supported so you can, should
 * you wish, pretend that async routines are simply Unity coroutines. If you don't
 * intend to use any of the special async routine functonality, though, it is
 * recommended to simply use Unity's vanilla coroutines.
 *
 * You can create new async instructions easily by inheriting from AsyncInstruction.
 * Look at the pre-existing instructions for a template on how to create an instruction.
 *
 * It is possible to throttle the game-synced execution of async routines have available.
 * Throttling is on by default, and set to a max of 25ms per frame.
 *
 * That's pretty much it. Enjoy!
 *
 */

using System;
using System.Collections;
using System.Collections.Generic;
using System.Reflection;
using System.Threading;
using UnityEngine;

/// <summary>
/// A singleton utility class for creating and managing asynchronous coroutines for easy, straight-forward multi-threading and parallellism.
/// </summary>
public sealed class Async : MonoBehaviour
{
    /// <summary>
    /// When yielded by an async routine, switches the routine to an asynchronous context running as a work item in the threadpool.
    /// </summary>
    public static readonly ToAsyncInstruction ToAsync = new ToAsyncInstruction();

    /// <summary>
    /// When yielded by an async routine, switches the routine to Unity's main game thread, running in the Update method.
    /// </summary>
    public static readonly ToGameInstruction ToGame = new ToGameInstruction();

    private static readonly System.Diagnostics.Stopwatch FrameTimer = new System.Diagnostics.Stopwatch();
    private static readonly object FrameTimerLock = new object();
    private static readonly object GameListsLock = new object();
    private static readonly object HandleLock = new object();
    private static readonly Dictionary<IEnumerator, AsyncRoutineHandle> RoutineToHandleMap = new Dictionary<IEnumerator, AsyncRoutineHandle>();
    private static readonly HashSet<IEnumerator> ToStopRoutines = new HashSet<IEnumerator>();
    private static readonly object ToStopRoutinesLock = new object();
    private static readonly FieldInfo UnityWaitForSecondsField = typeof(UnityEngine.WaitForSeconds).GetField("m_Seconds", BindingFlags.Public | BindingFlags.NonPublic | BindingFlags.Instance);
    private static readonly List<BaseWaitForConditionInstruction> WaitingForConditionRoutines = new List<BaseWaitForConditionInstruction>();
    private static readonly object WaitingForConditionRoutinesLock = new object();
    private static readonly List<IEnumerator> WaitingForFixedUpdateRoutines = new List<IEnumerator>();
    private static readonly object WaitingForFixedUpdateRoutinesLock = new object();
    private static readonly List<WaitingForRoutineRoutine> WaitingForRoutineRoutines = new List<WaitingForRoutineRoutine>();
    private static readonly object WaitingForRoutineRoutinesLock = new object();
    private static List<IEnumerator> GameListActive = new List<IEnumerator>();
    private static List<IEnumerator> GameListInactive = new List<IEnumerator>();
    private static Async instance;

    static Async()
    {
        // Constrain to 25ms per frame by default
        ThrottleGameLoopExecutionTime = true;
        ExecutionTimePerFrame = 0.025;
    }

    /// <summary>
    /// Gets the current <see cref="Async"/> singleton instance. If multiple instances exist, all but the first found instance will be destroyed.
    /// <para/>
    /// If an instance doesn't already exist, one will be created.
    /// The created <see cref="Async"/> instance and its <see cref="GameObject"/> will have the following <see cref="HideFlags"/> set:
    /// <see cref="HideFlags.NotEditable"/>,
    /// <see cref="HideFlags.DontSaveInBuild"/>,
    /// <see cref="HideFlags.DontSaveInEditor"/>,
    /// <see cref="HideFlags.HideInHierarchy"/>,
    /// <see cref="HideFlags.HideInInspector"/>.
    /// <para />
    /// The first time this is called, it *must* be called from Unity's main thread, as it uses Unity's API to find or create the current <see cref="Async"/> singleton instance.
    /// </summary>
    public static Async Instance
    {
        get
        {
            // No locks for this, since if this is being called outside of the
            // game loop and Async.instance is null, we're fucked anyway because
            // of Unity's threading restrictions.

            if (Async.instance == null)
            {
                Async[] asyncs = UnityEngine.Object.FindObjectsOfType<Async>();

                if (asyncs.Length == 0)
                {
                    GameObject go = new GameObject("Async");
                    Async.instance = go.AddComponent<Async>();

                    go.hideFlags = HideFlags.NotEditable | HideFlags.DontSaveInBuild | HideFlags.DontSaveInEditor | HideFlags.HideInHierarchy | HideFlags.HideInInspector;
                    Async.instance.hideFlags = go.hideFlags;
                }
                else if (asyncs.Length == 1)
                {
                    Async.instance = asyncs[0];
                }
                else
                {
                    Async.instance = asyncs[0];

                    Debug.LogError("Multiple async instances found. Destroying all superfluous instances. This should never happen.");

                    for (int i = 1; i < asyncs.Length; i++)
                    {
                        Debug.Log("Destroying async '" + asyncs[i].name + "'...", asyncs[i]);
                        UnityEngine.Object.Destroy(asyncs[i]);
                    }
                }
            }

            return Async.instance;
        }
    }

    /// <summary>
    /// Gets the execution context of the current thread.
    /// <para />
    /// Note that <see cref="ExecutionContext.Game"/> does not mean that the current thread is Unity's main thread (though it's likely that it is) - it merely means that the current thread is not a thread pool thread.
    /// </summary>
    public static ExecutionContext Context
    {
        get
        {
            return Thread.CurrentThread.IsThreadPoolThread ? ExecutionContext.Async : ExecutionContext.Game;
        }
    }

    /// <summary>
    /// Whether to render a simplistic immediate mode debug GUI that shows basic stats about the currently active async routines.
    /// </summary>
    public static bool RenderDebugGUI { get; set; }

    /// <summary>
    /// The amount of game-synced execution time, in seconds, allowed to be used by async routines doing work.
    /// <para />
    /// This is only enforced if <see cref="Async.ThrottleGameLoopExecutionTime"/> is set to true.
    /// </summary>
    public static double ExecutionTimePerFrame { get; set; }

    /// <summary>
    /// Whether to throttle game-synced async routines execution time to the value in <see cref="Async.ExecutionTimePerFrame"/>.
    /// </summary>
    public static bool ThrottleGameLoopExecutionTime { get; set; }

    private static double FrameTimeSpent
    {
        get
        {
            lock (Async.FrameTimerLock)
            {
                return Async.FrameTimer.Elapsed.TotalSeconds;
            }
        }
    }

    /// <summary>
    /// Gets the current <see cref="AsyncRoutineHandle"/> of the given <see cref="IEnumerator"/> routine, if it exists.
    /// <para />
    /// If the given enumerator is not currently an active async routine, this will be null.
    /// </summary>
    /// <param name="routine">The routine to get the handle of.</param>
    /// <returns>The current <see cref="AsyncRoutineHandle"/> of the given <see cref="IEnumerator"/> routine, if it exists; otherwise, null.</returns>
    public static AsyncRoutineHandle GetHandle(IEnumerator routine)
    {
        Debug.Assert(routine != null, "Routine argument is null");

        AsyncRoutineHandle handle;

        lock (Async.HandleLock)
        {
            Async.RoutineToHandleMap.TryGetValue(routine, out handle);
        }

        return handle;
    }

    /// <summary>
    /// Executes a given <see cref="Action{T}"/> once for each item in an <see cref="IEnumerable{T}"/>, in parallel.
    /// <para />
    /// Each item will be scheduled as a work item on the threadpool.
    /// </summary>
    /// <param name="source">The source of items.</param>
    /// <param name="body">The method to execute on each item in parallel.</param>
    /// <returns>A <see cref="WaitForConditionInstruction"/> that, when yielded, waits for the parallel work to complete before continuing.</returns>
    public static WaitForConditionInstruction ParallelForEach<T>(IEnumerable<T> source, Action<T> body)
    {
        int totalTasks = 0;
        int completedTasks = 0;

        foreach (var item in source)
        {
            totalTasks++;

            // Make sure we capture the item properly in the lambda enclosure;
            // I can't recall if it happens automatically in foreach loops in Unity
            T capturedItem = item;

            ThreadPool.QueueUserWorkItem((state) =>
            {
                try
                {
                    body(capturedItem);
                }
                catch (Exception ex)
                {
                    Debug.LogException(ex);
                }

                Interlocked.Increment(ref completedTasks);
            }, null);
        }

        return new WaitForConditionInstruction(() => completedTasks == totalTasks);
    }

    /// <summary>
    /// Starts an async routine in the current <see cref="ExecutionContext"/> and returns an <see cref="AsyncRoutineHandle"/> for that routine.
    /// </summary>
    /// <param name="routine">The routine to run.</param>
    /// <returns>A <see cref="AsyncRoutineHandle"/> for the routine.</returns>
    public static AsyncRoutineHandle Run(Func<IEnumerator> routine)
    {
        return Async.Run(routine(), Async.Context);
    }

    /// <summary>
    /// Starts an async routine in the given <see cref="ExecutionContext"/> and returns an <see cref="AsyncRoutineHandle"/> for that routine.
    /// </summary>
    /// <param name="routine">The routine to run.</param>
    /// <param name="context">The context to start the routine in.</param>
    /// <returns>A <see cref="AsyncRoutineHandle"/> for the routine.</returns>
    public static AsyncRoutineHandle Run(Func<IEnumerator> routine, ExecutionContext context)
    {
        return Async.Run(routine(), context);
    }

    /// <summary>
    /// Starts an async routine in the current <see cref="ExecutionContext"/> and returns an <see cref="AsyncRoutineHandle"/> for that routine.
    /// </summary>
    /// <param name="routine">The routine to run.</param>
    /// <param name="arg1">The first argument of the routine.</param>
    /// <returns>A <see cref="AsyncRoutineHandle"/> for the routine.</returns>
    public static AsyncRoutineHandle Run<TArg1>(Func<TArg1, IEnumerator> routine, TArg1 arg1)
    {
        return Async.Run(routine(arg1), Async.Context);
    }

    /// <summary>
    /// Starts an async routine in the given <see cref="ExecutionContext"/> and returns an <see cref="AsyncRoutineHandle"/> for that routine.
    /// </summary>
    /// <param name="routine">The routine to run.</param>
    /// <param name="context">The context to start the routine in.</param>
    /// <param name="arg1">The first argument of the routine.</param>
    /// <returns>A <see cref="AsyncRoutineHandle"/> for the routine.</returns>
    public static AsyncRoutineHandle Run<TArg1>(Func<TArg1, IEnumerator> routine, TArg1 arg1, ExecutionContext context)
    {
        return Async.Run(routine(arg1), context);
    }

    /// <summary>
    /// Starts an async routine in the current <see cref="ExecutionContext"/> and returns an <see cref="AsyncRoutineHandle"/> for that routine.
    /// </summary>
    /// <param name="routine">The routine to run.</param>
    /// <param name="arg1">The first argument of the routine.</param>
    /// <param name="arg2">The second argument of the routine.</param>
    /// <returns>A <see cref="AsyncRoutineHandle"/> for the routine.</returns>
    public static AsyncRoutineHandle Run<TArg1, TArg2>(Func<TArg1, TArg2, IEnumerator> routine, TArg1 arg1, TArg2 arg2)
    {
        return Async.Run(routine(arg1, arg2), Async.Context);
    }

    /// <summary>
    /// Starts an async routine in the given <see cref="ExecutionContext"/> and returns an <see cref="AsyncRoutineHandle"/> for that routine.
    /// </summary>
    /// <param name="routine">The routine to run.</param>
    /// <param name="context">The context to start the routine in.</param>
    /// <param name="arg1">The first argument of the routine.</param>
    /// <param name="arg2">The second argument of the routine.</param>
    /// <returns>A <see cref="AsyncRoutineHandle"/> for the routine.</returns>
    public static AsyncRoutineHandle Run<TArg1, TArg2>(Func<TArg1, TArg2, IEnumerator> routine, TArg1 arg1, TArg2 arg2, ExecutionContext context)
    {
        return Async.Run(routine(arg1, arg2), context);
    }

    /// <summary>
    /// Starts an async routine in the current <see cref="ExecutionContext"/> and returns an <see cref="AsyncRoutineHandle"/> for that routine.
    /// </summary>
    /// <param name="routine">The routine to run.</param>
    /// <param name="arg1">The first argument of the routine.</param>
    /// <param name="arg2">The second argument of the routine.</param>
    /// <param name="arg3">The third argument of the routine.</param>
    /// <returns>A <see cref="AsyncRoutineHandle"/> for the routine.</returns>
    public static AsyncRoutineHandle Run<TArg1, TArg2, TArg3>(Func<TArg1, TArg2, TArg3, IEnumerator> routine, TArg1 arg1, TArg2 arg2, TArg3 arg3)
    {
        return Async.Run(routine(arg1, arg2, arg3), Async.Context);
    }

    /// <summary>
    /// Starts an async routine in the given <see cref="ExecutionContext"/> and returns an <see cref="AsyncRoutineHandle"/> for that routine.
    /// </summary>
    /// <param name="routine">The routine to run.</param>
    /// <param name="context">The context to start the routine in.</param>
    /// <param name="arg1">The first argument of the routine.</param>
    /// <param name="arg2">The second argument of the routine.</param>
    /// <param name="arg3">The third argument of the routine.</param>
    /// <returns>A <see cref="AsyncRoutineHandle"/> for the routine.</returns>
    public static AsyncRoutineHandle Run<TArg1, TArg2, TArg3>(Func<TArg1, TArg2, TArg3, IEnumerator> routine, TArg1 arg1, TArg2 arg2, TArg3 arg3, ExecutionContext context)
    {
        return Async.Run(routine(arg1, arg2, arg3), context);
    }

    /// <summary>
    /// Starts an async routine in the current <see cref="ExecutionContext"/> and returns an <see cref="AsyncRoutineHandle"/> for that routine.
    /// </summary>
    /// <param name="routine">The routine to run.</param>
    /// <param name="arg1">The first argument of the routine.</param>
    /// <param name="arg2">The second argument of the routine.</param>
    /// <param name="arg3">The third argument of the routine.</param>
    /// <param name="arg4">The fourth argument of the routine.</param>
    /// <returns>A <see cref="AsyncRoutineHandle"/> for the routine.</returns>
    public static AsyncRoutineHandle Run<TArg1, TArg2, TArg3, TArg4>(Func<TArg1, TArg2, TArg3, TArg4, IEnumerator> routine, TArg1 arg1, TArg2 arg2, TArg3 arg3, TArg4 arg4)
    {
        return Async.Run(routine(arg1, arg2, arg3, arg4), Async.Context);
    }

    /// <summary>
    /// Starts an async routine in the given <see cref="ExecutionContext"/> and returns an <see cref="AsyncRoutineHandle"/> for that routine.
    /// </summary>
    /// <param name="routine">The routine to run.</param>
    /// <param name="context">The context to start the routine in.</param>
    /// <param name="arg1">The first argument of the routine.</param>
    /// <param name="arg2">The second argument of the routine.</param>
    /// <param name="arg3">The third argument of the routine.</param>
    /// <param name="arg4">The fourth argument of the routine.</param>
    /// <returns>A <see cref="AsyncRoutineHandle"/> for the routine.</returns>
    public static AsyncRoutineHandle Run<TArg1, TArg2, TArg3, TArg4>(Func<TArg1, TArg2, TArg3, TArg4, IEnumerator> routine, TArg1 arg1, TArg2 arg2, TArg3 arg3, TArg4 arg4, ExecutionContext context)
    {
        return Async.Run(routine(arg1, arg2, arg3, arg4), context);
    }

    /// <summary>
    /// Starts an async routine in the current <see cref="ExecutionContext"/> and returns an <see cref="AsyncRoutineHandle"/> for that routine.
    /// </summary>
    /// <param name="routine">The enumerator to run as a routine.</param>
    /// <returns>A <see cref="AsyncRoutineHandle"/> for the routine.</returns>
    public static AsyncRoutineHandle Run(IEnumerator routine)
    {
        return Async.Run(routine, Async.Context);
    }

    /// <summary>
    /// Starts an async routine in the given <see cref="ExecutionContext"/> and returns an <see cref="AsyncRoutineHandle"/> for that routine.
    /// </summary>
    /// <param name="routine">The enumerator to run as a routine.</param>
    /// <param name="context">The context to start the routine in.</param>
    /// <returns>A <see cref="AsyncRoutineHandle"/> for the routine.</returns>
    public static AsyncRoutineHandle Run(IEnumerator routine, ExecutionContext context)
    {
        Debug.Assert(routine != null, "Routine argument is null.");
        Debug.Assert(Async.GetHandle(routine) == null, "Routine to run is already an active async routine!");

        Async.TryEnsureSingleInstance();

        AsyncRoutineHandle handle = Async.GetOrCreateHandle(routine);

        switch (context)
        {
            case ExecutionContext.Game:
                Async.AdvanceRoutine(routine, ExecutionContext.Game, true);
                break;

            case ExecutionContext.Async:
                Async.QueueAsyncWork(routine);
                break;

            default:
                throw new NotImplementedException(context.ToString());
        }

        // If the routine finished instantly, it might have been deleted from the handle map already.
        // This is okay.

        return handle;
    }

    /// <summary>
    /// Registers that a given routine should be stopped as soon as possible.
    /// <para />
    /// Note that this will not take effect until the next time the routine yields or is set to be continued.
    /// Routines doing lengthy asynchronous work will not be halted mid-execution, and should yield return null occasionally to accomodate being stopped.
    /// </summary>
    /// <param name="routine">The routine to stop.</param>
    public static void StopRoutine(IEnumerator routine)
    {
        Debug.Assert(routine != null, "Routine argument is null");

        lock (Async.ToStopRoutinesLock)
        {
            Async.ToStopRoutines.Add(routine);
        }
    }

    /// <summary>
    /// Registers that a given routine should be stopped as soon as possible.
    /// <para />
    /// Note that this will not take effect until the next time the routine yields or is set to be continued.
    /// Routines doing lengthy asynchronous work will not be halted mid-execution, and should yield return null occasionally to accomodate being stopped.
    /// </summary>
    /// <param name="handle">The handle of the routine to stop.</param>
    public static void StopRoutine(AsyncRoutineHandle handle)
    {
        Debug.Assert(handle != null, "Handle argument is null");

        lock (Async.ToStopRoutinesLock)
        {
            Async.ToStopRoutines.Add(handle.Routine);
        }
    }

    /// <summary>
    /// Advances a routine by one step and processes the yielded value.
    ///
    /// This method also checks if the routine should be stopped, and
    /// removes stopped, errored and finished routines from the handle map.
    /// </summary>
    /// <param name="routine">The routine to advance.</param>
    /// <param name="currentContext">The current context.</param>
    /// <param name="allowThrottling">Whether to allow throttling the routine.</param>
    private static void AdvanceRoutine(IEnumerator routine, ExecutionContext currentContext, bool allowThrottling)
    {
        bool movedNext = false;
        bool stopped = false;

        if (allowThrottling && currentContext == ExecutionContext.Game && Async.ThrottleGameLoopExecutionTime && Async.FrameTimeSpent >= Async.ExecutionTimePerFrame)
        {
            Async.QueueGameWork(routine);
            return;
        }

        lock (Async.ToStopRoutinesLock)
        {
            if (Async.ToStopRoutines.Remove(routine))
            {
                stopped = true;
            }
        }

        if (!stopped)
        {
            try
            {
                movedNext = routine.MoveNext();
            }
            catch (Exception ex)
            {
                Debug.LogException(ex);
                movedNext = false;
            }
        }

        // Also check whether we should stop it after the routine has finished its current step
        // This is important for lengthy async routines
        lock (Async.ToStopRoutinesLock)
        {
            if (Async.ToStopRoutines.Remove(routine))
            {
                movedNext = false;
                stopped = true;
            }
        }

        if (movedNext)
        {
            object current = routine.Current;

            // Now we try to figure out what to do next, from the value that was yielded
            // This is just a long, ugly chain of if-else's

            if (object.ReferenceEquals(current, null))
            {
                if (Async.Context == ExecutionContext.Game)
                {
                    Async.QueueGameWork(routine);
                }
                else
                {
                    Async.QueueAsyncWork(routine);
                }
            }
            else
            {
                var instruction = current as IAsyncInstruction;

                if (instruction != null)
                {
                    instruction.Execute(routine);
                }
                else
                {
                    AsyncRoutineHandle waitForHandle = current as AsyncRoutineHandle;

                    if (waitForHandle == null)
                    {
                        var waitForRoutine = current as IEnumerator;

                        if (waitForRoutine != null)
                        {
                            waitForHandle = Async.GetHandle(waitForRoutine);

                            if (waitForHandle == null)
                            {
                                // It wasn't already a routine; run it
                                waitForHandle = Async.Run(waitForRoutine, currentContext);
                            }
                        }
                    }

                    if (waitForHandle != null)
                    {
                        lock (Async.WaitingForRoutineRoutinesLock)
                        {
                            Async.WaitingForRoutineRoutines.Add(new WaitingForRoutineRoutine(routine, waitForHandle));
                        }
                    }
                    else
                    {
                        var unityYieldInstruction = current as YieldInstruction;
                        bool succeeded = false;

                        if (unityYieldInstruction != null)
                        {
                            if (current is UnityEngine.WaitForSeconds)
                            {
                                if (Async.UnityWaitForSecondsField == null)
                                {
                                    Debug.LogError("Could not find Unity field 'WaitForSeconds.m_Seconds'; therefore you cannot currently yield Unity's WaitForSeconds class in AsyncRoutines. Use 'yield return new Async.WaitForSeconds(time)' instead.");
                                }
                                else
                                {
                                    var waitForSeconds = new Async.WaitForSeconds((float)Async.UnityWaitForSecondsField.GetValue(current));
                                    (waitForSeconds as IAsyncInstruction).Execute(routine);
                                    succeeded = true;
                                }
                            }
                            else if (current is WaitForFixedUpdate)
                            {
                                lock (Async.WaitingForFixedUpdateRoutinesLock)
                                {
                                    Async.WaitingForFixedUpdateRoutines.Add(routine);
                                }

                                succeeded = true;
                            }
                            else if (current is WaitForEndOfFrame)
                            {
                                if (currentContext == ExecutionContext.Async)
                                {
                                    Debug.LogError("Cannot wait for end of frame from an async context.");
                                }
                                else
                                {
                                    // Start a standard Unity coroutine to wait for end of frame.
                                    // It's the only way.
                                    Async.Instance.StartCoroutine(Async.Instance.WaitForEndOfFrameCoroutine(routine, (WaitForEndOfFrame)current));
                                }

                                succeeded = true;
                            }
                        }

                        if (!succeeded)
                        {
                            throw new InvalidAsyncInstructionException(current, routine);
                        }
                    }
                }
            }
        }
        else
        {
            // It's finished; remove it from the map
            lock (Async.HandleLock)
            {
                Async.RoutineToHandleMap.Remove(routine);
            }
        }
    }

    /// <summary>
    /// Gets the handle of a routine, or creates one if one doesn't already exist.
    /// </summary>
    /// <param name="routine">The routine to create a handle for.</param>
    /// <returns>The handle of the routine.</returns>
    private static AsyncRoutineHandle GetOrCreateHandle(IEnumerator routine)
    {
        AsyncRoutineHandle handle;

        lock (Async.HandleLock)
        {
            Async.RoutineToHandleMap.TryGetValue(routine, out handle);

            if (handle == null)
            {
                handle = new AsyncRoutineHandle(routine);
                Async.RoutineToHandleMap.Add(routine, handle);
            }
        }

        return handle;
    }

    /// <summary>
    /// Queues async work on the thread pool.
    /// </summary>
    /// <param name="routine">The routine to queue work for.</param>
    private static void QueueAsyncWork(IEnumerator routine)
    {
        ThreadPool.QueueUserWorkItem((state) =>
        {
            AdvanceRoutine(routine, ExecutionContext.Async, false);
        });
    }

    /// <summary>
    /// Queues a routine to continue on the game thread.
    /// </summary>
    /// <param name="routine">The routine to queue.</param>
    private static void QueueGameWork(IEnumerator routine)
    {
        lock (Async.GameListsLock)
        {
            Async.GameListActive.Add(routine);
        }
    }

    /// <summary>
    /// Tries to ensure that the <see cref="Async"/> singleton is created if it doesn't exist.
    /// Will do nothing if <see cref="Async.Context"/> is not <see cref="ExecutionContext.Game"/>.
    /// </summary>
    private static void TryEnsureSingleInstance()
    {
        if (Async.instance == null && Async.Context == ExecutionContext.Game)
        {
            // Just get the Instance property
            var inst = Async.Instance;

            // This merely exists to get rid of any compiler warnings
            if (inst != null)
            {
                inst = null;
            }
        }
    }

    /// <summary>
    /// Advances routines that are waiting for fixed update.
    /// </summary>
    private void FixedUpdate()
    {
        lock (Async.WaitingForFixedUpdateRoutinesLock)
        {
            for (int i = 0; i < Async.WaitingForFixedUpdateRoutines.Count; i++)
            {
                Async.AdvanceRoutine(Async.WaitingForFixedUpdateRoutines[i], ExecutionContext.Game, false);
            }

            Async.WaitingForFixedUpdateRoutines.Clear();
        }
    }

    /// <summary>
    /// Renders the debug GUI if <see cref="Async.RenderDebugGUI"/> is true.
    /// </summary>
    private void OnGUI()
    {
        if (Async.RenderDebugGUI)
        {
            lock (Async.HandleLock)
            {
                GUILayout.Label("Active routines: " + Async.RoutineToHandleMap.Count);
            }

            lock (Async.GameListsLock)
            {
                GUILayout.Label("Active game loop: " + Async.GameListActive.Count);
            }

            lock (Async.WaitingForConditionRoutinesLock)
            {
                GUILayout.Label("Waiting for condition: " + Async.WaitingForConditionRoutines.Count);
            }

            lock (Async.WaitingForRoutineRoutinesLock)
            {
                GUILayout.Label("Waiting for other routines: " + Async.WaitingForRoutineRoutines.Count);
            }

            lock (Async.WaitingForFixedUpdateRoutinesLock)
            {
                GUILayout.Label("Waiting for fixed update: " + Async.WaitingForFixedUpdateRoutines.Count);
            }
        }
    }

    /// <summary>
    /// Checks conditions for waiting routines to continue, and advances all game context async routines.
    /// </summary>
    private void Update()
    {
        lock (Async.GameListsLock)
        {
            // Switch active and inactive list, so we can iterate over the now inactive
            // list in peace while the now active list gathers up new requests until the
            // next frame.

            var temp = Async.GameListActive;
            Async.GameListActive = Async.GameListInactive;
            Async.GameListInactive = temp;
        }

        var list = Async.GameListInactive;

        // Handle routines that are waiting for other routines to complete
        // This code should run very fast as we're just performing a bunch of
        // dictionary lookups and removing items from a list.
        //
        // We can feel okay about holding the lock for the entire time.
        lock (Async.WaitingForRoutineRoutinesLock)
        {
            for (int i = 0; i < Async.WaitingForRoutineRoutines.Count; i++)
            {
                var waitingRoutine = Async.WaitingForRoutineRoutines[i];

                if (Async.GetHandle(waitingRoutine.WaitingForHandle.Routine) == null)
                {
                    // Routine it's waiting for doesn't exist any more; it can now continue.
                    // Remove it from the waiting list and schedule it for execution again.
                    Async.WaitingForRoutineRoutines.RemoveAt(i);
                    i--;

                    if (waitingRoutine.WaitingContext == ExecutionContext.Game)
                    {
                        list.Add(waitingRoutine.Routine);
                    }
                    else
                    {
                        Async.QueueAsyncWork(waitingRoutine.Routine);
                    }
                }
            }
        }

        // Handle routines that are waiting for a certain condition to be fulfilled
        // We are likely just checking a few conditions; this should hopefully be
        // quite fast.
        //
        // We feel less okay about holding the lock for the entire time, but still okay enough.
        lock (Async.WaitingForConditionRoutinesLock)
        {
            for (int i = 0; i < Async.WaitingForConditionRoutines.Count; i++)
            {
                var waitingRoutine = Async.WaitingForConditionRoutines[i];

                if (waitingRoutine.IsConditionFulfilled())
                {
                    // The waiting routine's condition has been fulfilled; it can now continue.
                    // Remove it from the waiting list and schedule it for execution again.
                    Async.WaitingForConditionRoutines.RemoveAt(i);
                    i--;

                    if (waitingRoutine.WaitingContext == ExecutionContext.Game)
                    {
                        list.Add(waitingRoutine.Routine);
                    }
                    else
                    {
                        Async.QueueAsyncWork(waitingRoutine.Routine);
                    }
                }
            }
        }

        // We're about to begin executing the actual game update routines, so start the timer.
        lock (Async.FrameTimerLock)
        {
            Async.FrameTimer.Reset();
            Async.FrameTimer.Start();
        }

        for (int i = 0; i < list.Count; i++)
        {
            Async.AdvanceRoutine(list[i], ExecutionContext.Game, true);
        }

        // Clear the list to free completed routines for GC.
        // Routines that ought to be continued have been added to the currently active list.
        list.Clear();
    }

    /// <summary>
    /// A vanilla Unity coroutine that waits for end of frame, then advances an async routine that yielded <see cref="WaitForEndOfFrame"/>.
    /// </summary>
    private IEnumerator WaitForEndOfFrameCoroutine(IEnumerator routine, WaitForEndOfFrame waitInstance)
    {
        yield return waitInstance;
        Async.AdvanceRoutine(routine, ExecutionContext.Game, false);
    }

    /// <summary>
    /// Data structure for routines that are waiting for other routines to finish before continuing.
    /// <para />
    /// This is similar to using a <see cref="BaseWaitForConditionInstruction"/>, only this common use case for waiting doesn't allocate any garbage.
    /// </summary>
    private struct WaitingForRoutineRoutine
    {
        /// <summary>
        /// The routine that is waiting.
        /// </summary>
        public readonly IEnumerator Routine;

        /// <summary>
        /// The context that waiting started in. This is the context that the routine will continue in when it is done waiting.
        /// </summary>
        public readonly ExecutionContext WaitingContext;

        /// <summary>
        /// The handle of the routine that is being waited for.
        /// </summary>
        public readonly AsyncRoutineHandle WaitingForHandle;

        public WaitingForRoutineRoutine(IEnumerator routine, AsyncRoutineHandle waitingForHandle)
        {
            this.Routine = routine;
            this.WaitingForHandle = waitingForHandle;
            this.WaitingContext = Async.Context;
        }
    }

    /// <summary>
    /// When yielded by an async routine, switches the routine to an asynchronous context running as a work item in the threadpool.
    /// <para />
    /// Never create an instance of this class; instead, use <see cref="Async.ToAsync"/>.
    /// </summary>
    public sealed class ToAsyncInstruction : AsyncInstruction
    {
        protected override void Execute(IEnumerator routine)
        {
            Async.QueueAsyncWork(routine);
        }
    }

    /// <summary>
    /// When yielded by an async routine, switches the routine to Unity's main game thread, running in the Update method.
    /// <para />
    /// Never create an instance of this class; instead, use <see cref="Async.ToGame"/>.
    /// </summary>
    public sealed class ToGameInstruction : AsyncInstruction
    {
        protected override void Execute(IEnumerator routine)
        {
            Async.QueueGameWork(routine);
        }
    }

    /// <summary>
    /// When yielded by an async routine, this abstract instruction waits until a condition defined by an inheriting type is fulfilled before continuing.
    /// </summary>
    public abstract class BaseWaitForConditionInstruction : AsyncInstruction
    {
        private int hasBeenExecuted = 0;

        /// <summary>
        /// The routine that is waiting.
        /// </summary>
        public IEnumerator Routine { get; private set; }

        /// <summary>
        /// The context in which waiting began. This is the context in which the routine will continue when the condition is fulfilled.
        /// </summary>
        public ExecutionContext WaitingContext { get; private set; }

        /// <summary>
        /// Determines whether the condition to wait for has been fulfilled.
        /// </summary>
        /// <returns>True if the condition has been fulfilled; otherwise, false.</returns>
        public abstract bool IsConditionFulfilled();

        protected sealed override void Execute(IEnumerator routine)
        {
            bool executed = Interlocked.CompareExchange(ref this.hasBeenExecuted, 1, 0) != 0;

            if (executed)
            {
                throw new InvalidOperationException("Can't yield the same " + this.GetType().Name + " twice.");
            }

            this.Routine = routine;
            this.WaitingContext = Async.Context;

            lock (Async.WaitingForConditionRoutinesLock)
            {
                Async.WaitingForConditionRoutines.Add(this);
            }
        }
    }

    /// <summary>
    /// When yielded, waits for a condition defined by a given delegate to become true before continuing.
    /// </summary>
    public sealed class WaitForConditionInstruction : BaseWaitForConditionInstruction
    {
        private Func<bool> completedCondition;

        /// <summary>
        /// Creates a new instance of the <see cref="WaitForConditionInstruction"/> class with a given condition delegate.
        /// </summary>
        /// <param name="completedCondition">The condition to wait for.</param>
        public WaitForConditionInstruction(Func<bool> completedCondition)
        {
            Debug.Assert(completedCondition != null, "Completed condition parameter is null");
            this.completedCondition = completedCondition;
        }

        /// <summary>
        /// Determines whether the condition to wait for has been fulfilled.
        /// </summary>
        /// <returns>True if the condition has been fulfilled; otherwise, false.</returns>
        public override bool IsConditionFulfilled()
        {
            return this.completedCondition();
        }
    }

    /// <summary>
    /// When yielded, waits for a number of seconds to pass, before continuing again.
    /// </summary>
    public sealed class WaitForSeconds : BaseWaitForConditionInstruction
    {
        private float time;

        /// <summary>
        /// The <see cref="Time.time"/> at which the routine will continue.
        /// </summary>
        public float ContinueTime { get { return this.time; } }

        /// <summary>
        /// Creates a new instance of the <see cref="WaitForSeconds"/> class which will wait for a given number of seconds.
        /// </summary>
        /// <param name="seconds">The number of seconds to wait.</param>
        public WaitForSeconds(float seconds)
        {
            Debug.Assert(seconds >= 0, "Seconds must be larger than or equal to zero");
            this.time = Time.time + seconds;
        }

        /// <summary>
        /// Determines whether the amount of time to wait has passed yet.
        /// </summary>
        /// <returns>True if the time has passed; otherwise, false.</returns>
        public override bool IsConditionFulfilled()
        {
            return Time.time >= this.time;
        }
    }
}

/// <summary>
/// The execution context of a thread.
/// </summary>
public enum ExecutionContext
{
    /// <summary>
    /// The context is not a thread pool thread. Note that this does not mean a thread with this context is Unity's main thread, though it is likely to be in most cases.
    /// </summary>
    Game,

    /// <summary>
    /// The context is a thread pool thread running asynchronously.
    /// </summary>
    Async
}

/// <summary>
/// An async instruction which is executed when yielded by an async routine.
/// </summary>
public interface IAsyncInstruction
{
    void Execute(IEnumerator routine);
}

/// <summary>
/// An async instruction which is executed when yielded by an async routine.
/// </summary>
public abstract class AsyncInstruction : IAsyncInstruction
{
    void IAsyncInstruction.Execute(IEnumerator routine)
    {
        this.Execute(routine);
    }

    protected abstract void Execute(IEnumerator routine);
}

/// <summary>
/// A handle for an async routine. Get a routine's handle by calling <see cref="Async.GetHandle(IEnumerator)"/>.
/// </summary>
public sealed class AsyncRoutineHandle : CustomYieldInstruction
{
    public readonly IEnumerator Routine;

    /// <summary>
    /// Creates a new instance of the <see cref="AsyncRoutineHandle"/> class for a given routine.
    /// <para />
    /// Note: don't create new instances of handles manually; handles will be allocated by the <see cref="Async"/> class when a routine is started.
    /// </summary>
    /// <param name="routine">The routine.</param>
    public AsyncRoutineHandle(IEnumerator routine)
    {
        Debug.Assert(routine != null, "Routine is null");
        this.Routine = routine;
    }

    /// <summary>
    /// This merely exists so that Unity's coroutines can wait for
    /// async routines. Ignore it.
    /// <para />
    /// This will return true if the AsyncRoutine is still active,
    /// and false if it has stopped.
    /// </summary>
    public override bool keepWaiting
    {
        get
        {
            return Async.GetHandle(this.Routine) != null;
        }
    }
}

/// <summary>
/// An exception that indicates that an invalid async instruction has been yielded.
/// </summary>
public class InvalidAsyncInstructionException : Exception
{
    private object invalidInstruction;
    private string message;
    private IEnumerator routine;

    /// <summary>
    /// Creates a new instance of the <see cref="InvalidAsyncInstructionException"/> class.
    /// </summary>
    /// <param name="invalidInstruction">The invalid instruction that was yielded.</param>
    /// <param name="routine">The routine that yielded the invalid instruction.</param>
    public InvalidAsyncInstructionException(object invalidInstruction, IEnumerator routine)
    {
        this.invalidInstruction = invalidInstruction;
        this.routine = routine;
        this.message = "An invalid async instruction of type '" + invalidInstruction.GetType() + "' was given by the routine '" + routine + "' of type '" + routine.GetType().FullName + "'.";
    }

    /// <summary>
    /// The invalid instruction instance that was yielded.
    /// </summary>
    public object InvalidInstruction { get { return this.invalidInstruction; } }

    /// <summary>
    /// The exception message.
    /// </summary>
    public override string Message { get { return this.message; } }

    /// <summary>
    /// The routine that yielded the invalid instruction.
    /// </summary>
    public IEnumerator Routine { get { return this.routine; } }
}