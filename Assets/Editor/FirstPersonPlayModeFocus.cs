using UnityEditor;
using UnityEngine;

// Give WASD/mouse input to the game once when Play starts, even if the Inspector
// was being edited. Never steal focus again while the designer is inspecting it.
[InitializeOnLoad]
internal static class FirstPersonPlayModeFocus
{
    static FirstPersonPlayModeFocus()
    {
        EditorApplication.playModeStateChanged += OnPlayModeChanged;
    }

    static void OnPlayModeChanged(PlayModeStateChange state)
    {
        if (state == PlayModeStateChange.EnteredPlayMode)
            EditorApplication.delayCall += FocusGame;
    }

    static void FocusGame()
    {
        if (!EditorApplication.isPlaying) return;
        var player = Object.FindObjectOfType<PlayerController>();
        if (!player || !player.isActiveAndEnabled) return;
        var gameViewType = typeof(EditorWindow).Assembly.GetType("UnityEditor.GameView");
        if (gameViewType == null) return;
        EditorWindow.GetWindow(gameViewType).Focus();
    }
}
