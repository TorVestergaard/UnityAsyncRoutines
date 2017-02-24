# UnityAsyncRoutines
An extremely lightweight Unity library for creating and managing asynchronous coroutines for easy, straight-forward multi-threading and parallellism

## Getting started

To get started, simply download AsyncRoutines.unitypackage from the root of the repository,
or clone the entire repository project. 

## Using Unity Async Routines

Async routines are basically fancy coroutines, only with an expanded set of
possible values to yield. Most fundamentally, yielding Async.ToAsync will
switch the method to run on a thread pool thread, and yield Async.ToGame will
return your method to the game thread.

This is very useful for the cases where you need to execute heave work, but
you also interact closely with Unity. This ease of switching between threads
is of great assistance when creating asynchronous code.

When you start an async routine using Async.Run, you will receive a handle.
This handle is a convenient way for you to talk about that instance of the
routine in particular. You can stop the routine with the handle, or you can
yield it in a regular, vanilla Unity coroutine and wait for the async routine
to finish.

All of Unity's own coroutine yield instructions are supported so you can, should
you wish, pretend that async routines are simply Unity coroutines. If you don't
intend to use any of the special async routine functonality, though, it is
recommended to simply use Unity's vanilla coroutines.

You can create new async instructions easily by inheriting from AsyncInstruction.
Look at the pre-existing instructions for a template on how to create an instruction.

It is possible to throttle the game-synced execution of async routines have available.
Throttling is on by default, and set to a max of 25ms per frame.

That's pretty much it. Enjoy!