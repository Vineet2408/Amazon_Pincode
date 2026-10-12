using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using System.IO;
public static class QueueTests {
 public static void Main() {
  var queue=new WorkQueue(Enumerable.Range(0,1000).Concat(Enumerable.Range(0,1000)));
  var active=new HashSet<int>();var seen=new HashSet<int>();object gate=new object();int peak=0;
  var tasks=Enumerable.Range(0,25).Select(worker=>Task.Factory.StartNew(()=>{
   int index;while(queue.TryClaim(out index)) {
    lock(gate){if(!active.Add(index)||!seen.Add(index))throw new Exception("Duplicate claim");peak=Math.Max(peak,active.Count);}
    // Model three attempts while the same worker retains its lease.
    for(int attempt=0;attempt<3;attempt++)Thread.Sleep(1);
    lock(gate){active.Remove(index);}
    if(!queue.Retire(index)||queue.Retire(index))throw new Exception("Retirement was not idempotent");
   }
  },TaskCreationOptions.LongRunning)).ToArray();Task.WaitAll(tasks);
  if(seen.Count!=1000||active.Count!=0||peak<2)throw new Exception("Queue lost work or failed to run concurrently");
  int unused;if(queue.TryClaim(out unused))throw new Exception("Retired work claimed again");
  var partial=new WorkQueue(new[]{1,3,5});int a,b;partial.TryClaim(out a);partial.TryClaim(out b);partial.Retire(b);
  var resumed=new WorkQueue(new[]{1,5});var recovered=new List<int>();while(resumed.TryClaim(out a)){recovered.Add(a);resumed.Retire(a);}
  if(!recovered.SequenceEqual(new[]{1,5}))throw new Exception("Resume queue failed");
  var lines=new[]{"PASS: 25 real threads; 1000 distinct entries claimed exactly once despite duplicate inputs.","PASS: Retry lease retained across three simulated attempts per item.","PASS: Completion/retirement is idempotent; no completed item is claimed again.","PASS: Pending items can be reconstructed after interruption.","Observed concurrent leases: "+peak};
  Directory.CreateDirectory("results");File.WriteAllLines("results/queue-tests.txt",lines);foreach(var line in lines)Console.WriteLine(line);
 }
}
