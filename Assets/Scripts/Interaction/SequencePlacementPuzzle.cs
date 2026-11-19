using UnityEngine;

// Can be reused for toy, block and jigsaw placement puzzles.
public class SequencePlacementPuzzle : MonoBehaviour
{
    [Min(1)] public int requiredSlots=3;
    public GameObject[] revealWhenSolved;
    [TextArea] public string successClue="物品按顺序放好了。";
    int next;
    public virtual bool CanPlace(int order)=>order==next;
    public virtual void OnPlaced(int order)
    {
        if(order!=next)return;
        next++;
        if(next<requiredSlots)return;
        if(revealWhenSolved!=null)foreach(var reward in revealWhenSolved)if(reward)reward.SetActive(true);
        if(StoryProgress.Instance)StoryProgress.Instance.RecordClue(successClue);
        OnSolved();
    }
    protected virtual void OnSolved(){}
}
