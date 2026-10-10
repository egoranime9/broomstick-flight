using UnityEngine;
using UnityEngine.InputSystem;

/// <summary>
/// Анимация модели руки от курка и грипа контроллера (перенесено из прошлого VR-проекта).
/// Аниматор руки должен иметь float-параметры "Trigger" и "Grip" — как в «Oculus Hands».
/// </summary>
public class HandAnimator : MonoBehaviour
{
    [SerializeField] private InputActionProperty triggerAction;
    [SerializeField] private InputActionProperty gripAction;
    [SerializeField] private Animator animator;

    static readonly int TriggerId = Animator.StringToHash("Trigger");
    static readonly int GripId = Animator.StringToHash("Grip");

    public void Configure(InputActionReference trigger, InputActionReference grip, Animator handAnimator)
    {
        triggerAction = new InputActionProperty(trigger);
        gripAction = new InputActionProperty(grip);
        animator = handAnimator;
    }

    private void Awake()
    {
        // Пальцы должны шевелиться и когда игра на паузе (в меню-шляпе время стоит)
        if (animator != null) animator.updateMode = AnimatorUpdateMode.UnscaledTime;
    }

    private void OnEnable()
    {
        // Ссылки на ассет экшенов включает Input Action Manager на XR Origin; «ручные» экшены включаем сами.
        if (triggerAction.reference == null) triggerAction.action?.Enable();
        if (gripAction.reference == null) gripAction.action?.Enable();
    }

    private void Update()
    {
        if (animator == null) return;
        animator.SetFloat(TriggerId, Read(triggerAction));
        animator.SetFloat(GripId, Read(gripAction));
    }

    static float Read(InputActionProperty property)
    {
        var action = property.action;
        return action != null ? action.ReadValue<float>() : 0f;
    }
}
