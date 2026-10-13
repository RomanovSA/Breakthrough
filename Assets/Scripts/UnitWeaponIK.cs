using UnityEngine;

/// <summary>
/// Makes the character hold its weapon with both hands, whatever the animation is doing.
/// The weapon is fixed to the chest; this script asks the Animator (humanoid IK) to put the hands on the
/// grip targets that sit on the weapon, and to bend the elbows towards the hint points.
/// Must be on the same object as the Animator, and the Animator controller layer needs 'IK Pass' enabled.
/// </summary>
[ExecuteAlways] // so the hand placement is also visible (and testable) in the editor, not only in Play mode
[RequireComponent(typeof(Animator))]
public class UnitWeaponIK : MonoBehaviour
{
    [Header("Hands (the wrist goes to the target)")]
    [SerializeField] private Transform rightHandTarget;
    [SerializeField] private Transform leftHandTarget;

    [Header("Elbows (they bend towards these points)")]
    [SerializeField] private Transform rightElbowHint;
    [SerializeField] private Transform leftElbowHint;

    [Range(0f, 1f)]
    [Tooltip("0 = the animation alone, 1 = the hands are fully on the weapon.")]
    [SerializeField] private float weight = 1f;

    private Animator animator;

    private void OnAnimatorIK(int layerIndex)
    {
        if (animator == null)
            animator = GetComponent<Animator>();

        Apply(AvatarIKGoal.RightHand, AvatarIKHint.RightElbow, rightHandTarget, rightElbowHint);
        Apply(AvatarIKGoal.LeftHand, AvatarIKHint.LeftElbow, leftHandTarget, leftElbowHint);
    }

    private void Apply(AvatarIKGoal goal, AvatarIKHint hint, Transform handTarget, Transform elbowHint)
    {
        if (handTarget == null)
        {
            animator.SetIKPositionWeight(goal, 0f);
            animator.SetIKHintPositionWeight(hint, 0f);
            return;
        }

        animator.SetIKPositionWeight(goal, weight);
        animator.SetIKPosition(goal, handTarget.position);

        if (elbowHint != null)
        {
            animator.SetIKHintPositionWeight(hint, weight);
            animator.SetIKHintPosition(hint, elbowHint.position);
        }
        else
        {
            animator.SetIKHintPositionWeight(hint, 0f);
        }
    }
}
