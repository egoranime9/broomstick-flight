using UnityEngine;
using UnityEngine.EventSystems;

/// <summary>Передаёт наведение и клик мышью по надписи пункта в <see cref="PauseMenu"/>.</summary>
public class PauseMenuItem : MonoBehaviour, IPointerEnterHandler, IPointerClickHandler
{
    private PauseMenu _menu;
    private int _index;

    public void Init(PauseMenu menu, int index)
    {
        _menu = menu;
        _index = index;
    }

    public void OnPointerEnter(PointerEventData eventData)
    {
        if (_menu != null) _menu.OnPointerEnterItem(_index);
    }

    public void OnPointerClick(PointerEventData eventData)
    {
        if (_menu != null && eventData.button == PointerEventData.InputButton.Left)
            _menu.OnPointerClickItem(_index);
    }
}
