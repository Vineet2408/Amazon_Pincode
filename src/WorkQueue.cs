using System.Collections.Generic;
using System.Linq;

// A claim remains leased to its worker for all retries. Deferred/finished entries
// cannot be picked again during this run; a new run retries only unfinished work.
public sealed class WorkQueue {
 readonly object gate=new object();
 readonly Queue<int> pending;
 readonly HashSet<int> leased=new HashSet<int>();
 readonly HashSet<int> retired=new HashSet<int>();
 public WorkQueue(IEnumerable<int> indices){pending=new Queue<int>(indices.Distinct());}
 public bool TryClaim(out int index){lock(gate){if(pending.Count==0){index=-1;return false;}index=pending.Dequeue();return leased.Add(index);}}
 public bool Retire(int index){lock(gate){if(!leased.Remove(index))return false;return retired.Add(index);}}
}
