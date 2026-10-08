// Stops and restarts Unity's frame loop for YouTube Playables' pause. Google's wrapper reports the
// pause to C# but cannot stop the loop, and Playables requires all execution -- rendering included --
// to stop between onPause and onResume. Unity's own web runtime provides both calls on Module.
mergeInto(LibraryManager.library, {
  FreeFlowPauseMainLoop: function () {
    if (typeof Module !== 'undefined' && typeof Module.pauseMainLoop === 'function') { Module.pauseMainLoop(); }
  },
  FreeFlowResumeMainLoop: function () {
    if (typeof Module !== 'undefined' && typeof Module.resumeMainLoop === 'function') { Module.resumeMainLoop(); }
  }
});
