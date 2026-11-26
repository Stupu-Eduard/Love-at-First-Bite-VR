using UnityEngine;
using UnityEngine.SceneManagement;

/// <summary>
/// Drives the start menu. Wired by the editor builder (Tools > Love at First Bite > Build Main Menu)
/// to the Play Game and Tutorial buttons.
///
/// Both buttons load the same gameplay scene. The difference is TutorialSession.StartInTutorial:
///  - Play Game: false -> normal play, the tutorial overlay stays dormant.
///  - Tutorial:  true  -> the TutorialManager in the scene runs the guided overlay.
/// </summary>
public class MainMenuController : MonoBehaviour
{
    [Tooltip("Name of the gameplay scene loaded by both buttons. Must be in Build Settings.")]
    [SerializeField] string m_GameplaySceneName = "SampleScene";

    [Tooltip("Optional: root holding the menu buttons.")]
    [SerializeField] GameObject m_MenuRoot;

    [Tooltip("Unused legacy placeholder; kept so older scene references don't break.")]
    [SerializeField] GameObject m_TutorialPanel;

    void Awake()
    {
        if (m_TutorialPanel != null) m_TutorialPanel.SetActive(false);
        if (m_MenuRoot != null) m_MenuRoot.SetActive(true);
    }

    /// <summary>Loads the gameplay scene for normal play. Hooked to the Play Game button.</summary>
    public void PlayGame()
    {
        TutorialSession.StartInTutorial = false;
        LoadGameplay();
    }

    /// <summary>Loads the gameplay scene with the guided tutorial on. Hooked to the Tutorial button.</summary>
    public void OpenTutorial()
    {
        TutorialSession.StartInTutorial = true;
        LoadGameplay();
    }

    void LoadGameplay()
    {
        if (string.IsNullOrEmpty(m_GameplaySceneName))
        {
            Debug.LogError("[MainMenu] No gameplay scene name set.", this);
            return;
        }
        SceneManager.LoadScene(m_GameplaySceneName);
    }

    /// <summary>Kept for the old placeholder's Back button; harmless if that panel is gone.</summary>
    public void BackToMenu()
    {
        if (m_TutorialPanel != null) m_TutorialPanel.SetActive(false);
        if (m_MenuRoot != null) m_MenuRoot.SetActive(true);
    }

    /// <summary>Called by the editor builder to set references without the Inspector.</summary>
    public void Configure(string gameplaySceneName, GameObject menuRoot, GameObject tutorialPanel)
    {
        m_GameplaySceneName = gameplaySceneName;
        m_MenuRoot = menuRoot;
        m_TutorialPanel = tutorialPanel;
    }
}
