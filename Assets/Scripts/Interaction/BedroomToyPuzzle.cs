using UnityEngine;
public sealed class BedroomToyPuzzle:SequencePlacementPuzzle
{
    public GameObject fragmentObject;
    protected override void OnSolved()
    {
        if(fragmentObject&&StoryProgress.Instance&&!StoryProgress.Instance.HasFirstFragment)
        {fragmentObject.SetActive(true);StoryProgress.Instance.RecordClue("四个玩偶按绘本顺序放回床上。书桌上出现了一张信封碎片。");}
    }
}
