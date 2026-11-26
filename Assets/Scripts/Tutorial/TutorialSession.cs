/// <summary>
/// One shared flag that survives a scene load, so the menu can tell the gameplay scene to start
/// in tutorial mode. Play Game leaves it false (normal play); Tutorial sets it true.
///
/// A plain static is enough: it is set right before SceneManager.LoadScene and read in the next
/// scene's Awake, all on the main thread.
/// </summary>
public static class TutorialSession
{
    /// <summary>True when the gameplay scene should run the guided tutorial overlay.</summary>
    public static bool StartInTutorial;
}
