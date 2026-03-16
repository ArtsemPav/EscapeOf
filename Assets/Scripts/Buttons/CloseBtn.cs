using UnityEngine;

public class CloseBtn : BaseButton {
    protected override void OnClick() {
        Application.Quit();
    }
}
