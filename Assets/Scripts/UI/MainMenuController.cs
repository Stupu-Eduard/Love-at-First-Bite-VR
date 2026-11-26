using UnityEngine;
using UnityEngine.SceneManagement;

/// <summary>
/// Drives the start menu. Wired by the editor builder (Tools > Love at First Bite > Build Main Menu)
/// to the Play Game and Tutorial buttons.
///
/// - Play Game: loads the gameplay scene (the current SampleScene) by name.
/// - Tutorial:  for now only swaps the menu root for a placeholder panel. The real tutorial
///   comes later; BackToMenu() returns to the buttons.
/// </summary>
public class MainMenuController : MonoBehaviour
{
    [Tooltip("Name of the gameplay scene loaded by Play Game. Must be in Build Settings.")]
    [SerializeField] string m_GameplaySceneName = "SampleScene";

    [Tooltip("Root holding the menu buttons. Hidden while the tutorial placeholder is shown.")]
    [SerializeField] GameObject m_MenuRoot;

    [Tooltip("Placeholder panel shown by Tutorial. Empty for now.")]
    [SerializeField] GameObject m_TutorialPanel;

    void Awake()
    {
        if (m_TutorialPanel != null) m_TutorialPanel.SetActive(false);
        if (m_MenuRoot != null) m_MenuRoot.SetActive(true);
    }

    /// <summary>Loads the gameplay scene. Hooked to the Play Game button.</summary>
    public void PlayGame()
    {
        if (string.IsNullOrEmpty(m_GameplaySceneName))
        {
            Debug.LogError("[MainMenu] No gameplay scene name set.", this);
            return;
        }
        SceneManager.LoadScene(m_GameplaySceneName);
    }

    /// <summary>Shows the (empty) tutorial placeholder. Hooked to the Tutorial button.</summary>
    public void OpenTutorial()
    {
        if (m_MenuRoot != null) m_MenuRoot.SetActive(false);
        if (m_TutorialPanel != null) m_TutorialPanel.SetActive(true);
    }

    /// <summary>Returns from the tutorial placeholder to the menu buttons.</summary>
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
