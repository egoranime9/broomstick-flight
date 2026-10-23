using System;
using System.Collections.Generic;
using UnityEngine;

/// <summary>
/// Система уровней. Карта одна, уровни — дочерние объекты с компонентом Level;
/// включён только текущий. Менеджер:
///   * засчитывает пролёт колец (только верхом на метле), ведёт таймер;
///   * показывает подсказку перед глазами и стрелку к следующей цели;
///   * сохраняет лучшее время и сколько уровней открыто (PlayerPrefs) — пригодится для меню выбора уровня.
/// </summary>
public class LevelManager : MonoBehaviour
{
    public enum GuideStyle { Arrow3D, Persona }

    [Tooltip("С какого уровня начинать (0 — первый)")]
    [SerializeField] private int startLevel = 0;
    [SerializeField] private BroomController broom;
    [SerializeField] private Transform head;
    [Tooltip("Как показывать следующую цель: золотая 3D-стрелка или указатель в стиле Persona")]
    [SerializeField] private GuideStyle guide = GuideStyle.Arrow3D;
    [Tooltip("Через сколько секунд после приземления спрятать итог уровня")]
    [SerializeField] private float resultShowTime = 10f;

    public static LevelManager Instance { get; private set; }

    public IReadOnlyList<Level> Levels => _levels;
    public int CurrentIndex => _index;
    public Level Current => _index >= 0 && _index < _levels.Count ? _levels[_index] : null;

    public event Action<Level> LevelStarted;
    public event Action<Level> LevelCompleted;

    public const string UnlockedKey = "Broom_LevelsUnlocked";
    public static string BestTimeKey(int index) => $"Broom_Level{index + 1}_Best";

    readonly List<Level> _levels = new List<Level>();
    int _index = -1;
    Vector3 _previousHead;
    bool _completed;
    float _landedAt = -1f;
    HintPanel _hint;
    GuideArrow _arrow;
    PersonaGuide _persona;

    void Awake() => Instance = this;

    void Start()
    {
        if (broom == null) broom = FindAnyObjectByType<BroomController>();
        if (head == null && Camera.main != null) head = Camera.main.transform;

        GetComponentsInChildren(true, _levels);
        _hint = HintPanel.Create(transform, head);
        if (guide == GuideStyle.Persona) _persona = PersonaGuide.Create(transform, head);
        else _arrow = GuideArrow.Create(transform, head);

        if (_levels.Count == 0) Debug.LogWarning("[Levels] Нет ни одного уровня — запусти Broom → Setup Levels.");
        else StartLevel(startLevel);
    }

    public void StartLevel(int index)
    {
        if (_levels.Count == 0) return;
        _index = Mathf.Clamp(index, 0, _levels.Count - 1);
        for (int i = 0; i < _levels.Count; i++)
            _levels[i].gameObject.SetActive(i == _index);

        Current.Begin();
        _completed = false;
        _landedAt = -1f;
        if (head != null) _previousHead = head.position;
        LevelStarted?.Invoke(Current);
    }

    public void RestartLevel() => StartLevel(_index);

    void LateUpdate()
    {
        var level = Current;
        if (level == null || head == null) return;

        float dt = Time.deltaTime;
        bool mounted = broom != null && broom.IsMounted;
        Vector3 headNow = head.position;

        if (dt > 0f)
        {
            if (mounted && !level.TimerRunning && !level.IsFinished) level.StartTimer();
            level.Tick(dt);

            if (mounted && level.TryPass(_previousHead, headNow))
            {
                broom.Pulse(0.5f, 0.12f);
                if (level.IsFinished && !_completed) Complete(level);
            }
        }
        _previousHead = headNow;

        UpdateGuidance(level, mounted);
    }

    void Complete(Level level)
    {
        _completed = true;

        string key = BestTimeKey(_index);
        float best = PlayerPrefs.GetFloat(key, float.MaxValue);
        if (level.Elapsed < best) PlayerPrefs.SetFloat(key, level.Elapsed);
        PlayerPrefs.SetInt(UnlockedKey, Mathf.Max(PlayerPrefs.GetInt(UnlockedKey, 1), Mathf.Min(_index + 2, _levels.Count)));
        PlayerPrefs.Save();

        LevelCompleted?.Invoke(level);
    }

    void UpdateGuidance(Level level, bool mounted)
    {
        bool paused = Time.timeScale < 0.5f;     // меню-шляпа открыто
        string title = $"{level.Title.ToUpperInvariant()}   {level.PassedCount}/{level.Rings.Count}";
        string body;
        bool visible = !paused;
        Vector3? target = null;
        string targetLabel = "";
        float markerLift = 1f;

        if (level.IsFinished)
        {
            string time = Level.FormatTime(level.Elapsed);
            if (mounted)
            {
                body = string.Format(level.FinishedInAirHint, time);
                _landedAt = -1f;
            }
            else
            {
                body = string.Format(level.FinishedHint, time);
                if (_landedAt < 0f) _landedAt = Time.unscaledTime;
                if (Time.unscaledTime - _landedAt > resultShowTime) visible = false;
            }
        }
        else if (!mounted)
        {
            body = level.PassedCount == 0 && !level.TimerRunning ? level.MountHint : level.RemountHint;
            if (broom != null) target = broom.transform.position;
            targetLabel = "BROOM";
            markerLift = 0.7f;
        }
        else
        {
            var ring = level.NextRing;
            body = ring != null && !string.IsNullOrEmpty(ring.Hint) ? ring.Hint : level.DefaultHint;
            if (ring != null)
            {
                target = ring.transform.position;
                targetLabel = $"NEXT {level.PassedCount + 1}/{level.Rings.Count}";
                markerLift = ring.WorldRadius + 1.5f;
            }
        }

        _hint.Show(title, body, visible);
        if (_arrow != null) _arrow.SetTarget(paused ? null : target);
        if (_persona != null) _persona.SetTarget(paused ? null : target, targetLabel, markerLift);
    }
}
