using UnityEngine;

public class ResumeBtn : BaseButton {
    protected override void OnClick() {
        if (GameManager.Instance == null)
        {
            Debug.LogError("ResumeBtn: GameManager.Instance is null.");
            return;
        }
        GameManager.Instance.SetPause(false);
    }
}
