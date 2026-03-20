using UnityEngine;

public class ResumeBtn : BaseButton {
    protected override void OnClick() {
        GameManager.Instance.SetPause(false);
    }
}
