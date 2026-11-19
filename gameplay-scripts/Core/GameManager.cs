using UnityEngine;
using UnityEngine.SceneManagement;

public enum GameState { Loading, Playing, LevelUp, Dead }

[DefaultExecutionOrder(-100)]
public class GameManager : MonoBehaviour
{
    public static GameManager Instance { get; private set; }
    public GameState State { get; private set; }

    void Awake()
    {
        if (Instance != null) { Destroy(gameObject); return; }
        Instance = this;
    }

    public void SetState(GameState state)
    {
        State = state;
        Time.timeScale = state == GameState.Playing ? 1f : 0f;
    }

    public void RestartGame()
    {
        Time.timeScale = 1f;
        SceneManager.LoadScene(SceneManager.GetActiveScene().buildIndex);
    }
}
