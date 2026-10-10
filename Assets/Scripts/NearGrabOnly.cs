using UnityEngine;
using UnityEngine.XR.Interaction.Toolkit.Filtering;
using UnityEngine.XR.Interaction.Toolkit.Interactables;
using UnityEngine.XR.Interaction.Toolkit.Interactors;

/// <summary>
/// Разрешает взять объект только рукой, поднесённой вплотную: дальний луч его не подсвечивает и не хватает.
/// Вешается рядом с XR Grab Interactable.
/// </summary>
[RequireComponent(typeof(XRGrabInteractable))]
public class NearGrabOnly : MonoBehaviour, IXRSelectFilter, IXRHoverFilter
{
    [Tooltip("Насколько близко (в метрах) рука должна быть к коллайдеру объекта, чтобы его можно было взять")]
    [SerializeField] private float maxDistance = 0.2f;

    private XRGrabInteractable _grab;
    private Collider[] _colliders;

    public bool canProcess => isActiveAndEnabled;

    private void Awake()
    {
        _grab = GetComponent<XRGrabInteractable>();
        _colliders = GetComponentsInChildren<Collider>();
    }

    private void OnEnable()
    {
        _grab.selectFilters.Add(this);
        _grab.hoverFilters.Add(this);
    }

    private void OnDisable()
    {
        _grab.selectFilters.Remove(this);
        _grab.hoverFilters.Remove(this);
    }

    public bool Process(IXRSelectInteractor interactor, IXRSelectInteractable interactable)
    {
        // Уже взятую метлу не отбираем, даже если рука чуть отошла
        return interactable.interactorsSelecting.Contains(interactor) || IsClose(interactor.transform.position);
    }

    public bool Process(IXRHoverInteractor interactor, IXRHoverInteractable interactable)
    {
        return IsClose(interactor.transform.position);
    }

    private bool IsClose(Vector3 handPosition)
    {
        foreach (var c in _colliders)
        {
            if (c == null || !c.enabled) continue;
            if ((c.ClosestPoint(handPosition) - handPosition).sqrMagnitude <= maxDistance * maxDistance)
                return true;
        }
        return false;
    }
}
