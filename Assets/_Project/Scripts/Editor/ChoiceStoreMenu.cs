using System.Collections.Generic;
using System.IO;
using Pickleball.Sandbox.Dialogue;
using UnityEditor;
using UnityEngine;

/// <summary>
/// Tools ▸ Pickleball ▸ Dialogue: this project's choice save from the Editor.
///
/// <list type="bullet">
/// <item><b>New Game (delete choices save)</b> deletes the save, so every
/// conversation treats the player as new again (Birdy's intro comes back).
/// In Play mode it also clears the live stores through
/// <see cref="ChoiceStoreHost.NewGame"/>, so they don't write the old state
/// back.</item>
/// <item><b>Reveal choices save</b> shows the file in Explorer / Finder, or,
/// before anything has been picked, logs where it will be created.</item>
/// </list>
///
/// In the Editor the save lives in a folder keyed to this project
/// (<see cref="ChoiceStoreSave.DefaultDirectory"/>:
/// <c>&lt;persistentDataPath&gt;/Saves/Editor/&lt;project&gt;-&lt;hash&gt;/</c>), so both
/// items act on this checkout's or worktree's save only, never another
/// worktree's or a built player's. They cover the default file name and the
/// file name of every <see cref="ChoiceStoreHost"/> in the open scenes.
/// Neither touches a scene or an asset.
/// </summary>
public static class ChoiceStoreMenu
{
    const string MenuRoot = "Tools/Pickleball/Dialogue/";

    [MenuItem(MenuRoot + "New Game (delete choices save)")]
    static void NewGame()
    {
        List<ChoiceStoreHost> hosts = OpenHosts();
        List<string> paths = SavePaths(hosts);
        List<string> existing = paths.FindAll(File.Exists);
        bool liveHosts = EditorApplication.isPlaying && hosts.Count > 0;

        if (existing.Count == 0 && !liveHosts)
        {
            Debug.Log($"ChoiceStoreMenu: this project has no choices save to delete (it would be at {string.Join(", ", paths)}).");
            return;
        }

        if (!Application.isBatchMode && !EditorUtility.DisplayDialog("New Game",
                "Delete this project's choices save? Every choice and fact is forgotten, " +
                "so NPCs treat the player as new.\n\n" + string.Join("\n", existing.Count > 0 ? existing : paths),
                "Delete", "Cancel"))
            return;

        if (EditorApplication.isPlaying)
            foreach (ChoiceStoreHost host in hosts)
                host.NewGame();

        var deleted = new List<string>();
        foreach (string path in paths)
        {
            bool existed = existing.Contains(path);
            if (ChoiceStoreSave.Delete(path, out string error))
            {
                if (existed) deleted.Add(path);
            }
            else
            {
                Debug.LogWarning($"ChoiceStoreMenu: {error}");
            }
        }

        Debug.Log(deleted.Count > 0
            ? $"ChoiceStoreMenu: new game. Deleted {string.Join(", ", deleted)}."
            : "ChoiceStoreMenu: new game. The live choice stores were cleared.");
    }

    [MenuItem(MenuRoot + "Reveal choices save")]
    static void Reveal()
    {
        List<string> paths = SavePaths(OpenHosts());
        string existing = paths.Find(File.Exists);
        if (existing != null)
        {
            Debug.Log($"ChoiceStoreMenu: this project's choices save is {existing}");
            EditorUtility.RevealInFinder(existing);
            return;
        }

        Debug.Log($"ChoiceStoreMenu: this project has no choices save yet. It is created at {paths[0]} once something is picked.");
        string folder = ChoiceStoreSave.DefaultDirectory;
        while (!string.IsNullOrEmpty(folder) && !Directory.Exists(folder)) folder = Path.GetDirectoryName(folder);
        if (!string.IsNullOrEmpty(folder)) EditorUtility.RevealInFinder(folder);
    }

    static List<ChoiceStoreHost> OpenHosts()
        => new(Object.FindObjectsByType<ChoiceStoreHost>(FindObjectsInactive.Include, FindObjectsSortMode.None));

    // The default save first, then each persisting host's (its live path in
    // Play mode, else its file name in this project's save folder).
    static List<string> SavePaths(List<ChoiceStoreHost> hosts)
    {
        var paths = new List<string>();
        AddFileName(paths, ChoiceStoreSave.DefaultFileName);
        foreach (ChoiceStoreHost host in hosts)
        {
            if (!host.Persist) continue;
            if (host.SavePath != null)
            {
                if (!paths.Contains(host.SavePath)) paths.Add(host.SavePath);
            }
            else
            {
                AddFileName(paths, host.FileName);
            }
        }
        return paths;
    }

    static void AddFileName(List<string> paths, string fileName)
    {
        if (ChoiceStoreSave.TryResolvePath(ChoiceStoreSave.DefaultDirectory, fileName, Application.dataPath,
                out string path, out string error))
        {
            if (!paths.Contains(path)) paths.Add(path);
        }
        else
        {
            Debug.LogWarning($"ChoiceStoreMenu: skipped a host's save: {error}");
        }
    }
}
