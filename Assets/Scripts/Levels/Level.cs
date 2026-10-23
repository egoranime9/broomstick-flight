using System.Collections.Generic;
using UnityEngine;

/// <summary>
/// Один уровень. Это пустой объект, в котором лежат все объекты уровня:
/// кольца (FlightRing) в порядке пролёта — сверху вниз по иерархии, — а в будущих уровнях препятствия, снитч и т. д.
/// Включён только объект текущего уровня, этим занимается LevelManager.
/// Если заполнить список «Порядок пролёта», кольца проходятся в его порядке, а не по иерархии.
/// </summary>
public class Level : MonoBehaviour
{
    [SerializeField] private string title = "Обучение";
    [Tooltip("Порядок пролёта колец. Пусто — по порядку в иерархии")]
    [SerializeField] private List<FlightRing> order = new List<FlightRing>();

    [Header("Подсказки")]
    [TextArea(1, 3)] [SerializeField] private string mountHint = "Подойди к метле и возьми её обеими руками";
    [TextArea(1, 3)] [SerializeField] private string remountHint = "Возьми метлу обеими руками, чтобы продолжить";
    [TextArea(1, 3)] [SerializeField] private string defaultHint = "Лети к светящемуся кольцу";
    [Tooltip("{0} — время прохождения")]
    [TextArea(1, 3)] [SerializeField] private string finishedInAirHint = "Пройдено за {0}!\nНажми A, чтобы приземлиться";
    [Tooltip("{0} — время прохождения")]
    [TextArea(1, 3)] [SerializeField] private string finishedHint = "Уровень пройден! Время: {0}";

    public string Title => title;
    public string MountHint => mountHint;
    public string RemountHint => remountHint;
    public string DefaultHint => defaultHint;
    public string FinishedInAirHint => finishedInAirHint;
    public string FinishedHint => finishedHint;

    public IReadOnlyList<FlightRing> Rings => _rings;
    public int PassedCount { get; private set; }
    public bool IsFinished => _rings.Count > 0 && PassedCount >= _rings.Count;
    public bool TimerRunning { get; private set; }
    public float Elapsed { get; private set; }
    public FlightRing NextRing => PassedCount < _rings.Count ? _rings[PassedCount] : null;

    readonly List<FlightRing> _rings = new List<FlightRing>();

    /// <summary>Сброс: все кольца снова ждут, первое — следующее, таймер на нуле.</summary>
    public void Begin()
    {
        _rings.Clear();
        foreach (var ring in order)
            if (ring != null && !_rings.Contains(ring)) _rings.Add(ring);
        if (_rings.Count == 0) GetComponentsInChildren(true, _rings);
        PassedCount = 0;
        Elapsed = 0f;
        TimerRunning = false;
        for (int i = 0; i < _rings.Count; i++)
            _rings[i].SetState(i == 0 ? FlightRing.RingState.Next : FlightRing.RingState.Waiting);
    }

    public void StartTimer() => TimerRunning = true;

    public void Tick(float dt)
    {
        if (TimerRunning && !IsFinished) Elapsed += dt;
    }

    /// <summary>Проверяет, пролетел ли игрок (отрезок движения головы) через следующее кольцо.</summary>
    public bool TryPass(Vector3 from, Vector3 to)
    {
        var ring = NextRing;
        if (ring == null || !ring.Crossed(from, to)) return false;

        ring.SetState(FlightRing.RingState.Passed);
        ring.PlayChime(PassedCount);
        PassedCount++;
        if (NextRing != null) NextRing.SetState(FlightRing.RingState.Next);
        return true;
    }

    public static string FormatTime(float seconds)
    {
        int minutes = (int)(seconds / 60f);
        float rest = seconds - minutes * 60f;
        return $"{minutes:00}:{rest:00.0}";
    }
}
