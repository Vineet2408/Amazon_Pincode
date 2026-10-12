using System;
using System.IO;
using System.Linq;
using System.Text;
using System.Text.RegularExpressions;
using System.Collections.Generic;
using System.Globalization;
using System.Drawing;
using System.Windows.Forms;
using System.Threading.Tasks;
using System.Threading;
using System.Web.Script.Serialization;
using Microsoft.Web.WebView2.WinForms;
using Microsoft.Web.WebView2.Core;

public class Snapshot {
 public bool blocked, ready, cart;
 public string href, title, location, delivery, availability, price, buybox, invalid, body;
}
public class Session {
 public string url, folder;
 public decimal baseline, tolerance;
 public int next, delay;
 public List<string> pins = new List<string>();
 public List<string> good_pincodes = new List<string>();
 public int version;
 public List<int> completed = new List<int>();
 public void Upgrade() {if(version<2){completed=Enumerable.Range(0,Math.Min(next,pins.Count)).ToList();version=2;}}
 public Queue<int> Pending() {var done=new HashSet<int>(completed);return new Queue<int>(Enumerable.Range(0,pins.Count).Where(i=>!done.Contains(i)));}
 public void Complete(int index) {if(!completed.Contains(index))completed.Add(index);RefreshNext();}
 public void RefreshNext() {var done=new HashSet<int>(completed);next=0;while(done.Contains(next))next++;}
}
public class Verdict {
 public string status, reason;
 public decimal? price, shipping;
 public Verdict() {}
 public Verdict(string s,string r,decimal? p,decimal? d) {status=s;reason=r;price=p;shipping=d;}
}
public static class Rules {
 public static string Asin(string url) { var m=Regex.Match(url ?? "", @"/(?:dp|gp/product)/([A-Z0-9]{10})(?:[/?]|$)",RegexOptions.IgnoreCase); return m.Success?m.Groups[1].Value.ToUpperInvariant():""; }
 public static bool Amazon(string url) { Uri u; return Uri.TryCreate(url,UriKind.Absolute,out u) && u.Scheme=="https" && (u.Host=="amazon.in" || u.Host.EndsWith(".amazon.in",StringComparison.OrdinalIgnoreCase)); }
 public static decimal? Money(string value) {var m=Regex.Match(value??"",@"(?:₹|INR|Rs\.?)\s*([\d,]+(?:\.\d{1,2})?)",RegexOptions.IgnoreCase); decimal n; return m.Success && decimal.TryParse(m.Groups[1].Value.Replace(",",""),NumberStyles.Number,CultureInfo.InvariantCulture,out n)?(decimal?)n:null;}
 public static Verdict Evaluate(Snapshot s,string pin,string asin,decimal maximum) {
   if(s.blocked) return new Verdict("REVIEW","Amazon requires a human check.",null,null);
   if(!Amazon(s.href) || Asin(s.href)!=asin || String.IsNullOrWhiteSpace(s.title)) return new Verdict("REVIEW","The requested product page is not loaded.",null,null);
   var locations=Regex.Matches(s.location??"",@"(?<!\d)[1-9]\d{5}(?!\d)").Cast<Match>().Select(m=>m.Value).Distinct().ToList();
   if(locations.Count!=1 || locations[0]!=pin) return new Verdict("REVIEW","Selected pincode could not be confirmed consistently.",null,null);
   string evidence=(s.delivery??"")+" "+(s.availability??"")+" "+(s.buybox??"");
   decimal? price=Money(s.price);
   if(Regex.IsMatch(evidence,@"cannot be (?:shipped|delivered)|can't be (?:shipped|delivered)|not deliverable|does not deliver|not available for delivery|unable to deliver|delivery (?:is )?not available",RegexOptions.IgnoreCase)) return new Verdict("NO_DELIVERY","Amazon says this location cannot receive this item.",price,null);
   if(Regex.IsMatch(s.availability??"",@"currently unavailable|out of stock|temporarily out of stock",RegexOptions.IgnoreCase)) return new Verdict("UNAVAILABLE","No stock is currently offered.",price,null);
   if(!price.HasValue || price<=0) return new Verdict("REVIEW","No unambiguous item price found.",price,null);
   if(price>maximum) return new Verdict("PRICE_TOO_HIGH","Item price exceeds your limit.",price,null);
   string delivery=s.delivery??"";
   bool positive=Regex.IsMatch(delivery,@"(?:delivery|arrives|get it|delivered).*(?:Monday|Tuesday|Wednesday|Thursday|Friday|Saturday|Sunday|today|tomorrow|\b\d{1,2}\b)",RegexOptions.IgnoreCase|RegexOptions.Singleline);
   if(!positive || !s.cart) return new Verdict("REVIEW","A delivery date and enabled Add to Cart were not both found.",price,null);
   decimal? shipping=null;
   if(Regex.IsMatch(delivery,@"FREE\s+(?:scheduled\s+)?delivery",RegexOptions.IgnoreCase) && !Regex.IsMatch(delivery,@"FREE delivery (?:on|for) orders",RegexOptions.IgnoreCase)) shipping=0;
   else {var m=Regex.Match(delivery,@"(?:₹|INR|Rs\.?)\s*[\d,]+(?:\.\d{1,2})?\s+(?:scheduled\s+)?delivery",RegexOptions.IgnoreCase); if(m.Success) shipping=Money(m.Value);}
   if(!shipping.HasValue) return new Verdict("REVIEW","Delivery charge is unclear; inspect the offer before accepting it.",price,null);
   return price+shipping<=maximum?new Verdict("GOOD","Delivery date, location, item price and shipping verified on the page.",price,shipping):new Verdict("PRICE_TOO_HIGH","Item plus displayed delivery charge exceeds your limit.",price,shipping);
 }
}
public class App : Form {
 const int WorkerCount=10;
 const string DefaultUrl="https://www.amazon.in/LG-Ultragear-10-27Gn800-Resolution-Compatible/dp/B0B5V5VHB6";
 readonly string root=AppDomain.CurrentDomain.BaseDirectory;
 readonly JavaScriptSerializer json=new JavaScriptSerializer {MaxJsonLength=8000000};
 WebView2 web=new WebView2(); TextBox url=new TextBox(), pins=new TextBox(), log=new TextBox();
 NumericUpDown baseline=new NumericUpDown(), tolerance=new NumericUpDown(), delay=new NumericUpDown();
 Label status=new Label(), counts=new Label(); Button start,stop,resume,load,open,skip; CheckBox first=new CheckBox();
 Session session; bool running, stopping, browserReady; string mode; int navigations;
 BrowserWorker[] workers;
 CancellationTokenSource runCancellation;
 string pauseReason;
 int reviewIndex=-1;
 public App(string mode) {
  this.mode=mode; Text="Amazon delivery finder"; Width=1450; Height=940; MinimumSize=new Size(1100,760); StartPosition=FormStartPosition.CenterScreen;
  Font=new Font("Segoe UI",10); BackColor=Color.FromArgb(246,248,251);
  var panel=new Panel {Dock=DockStyle.Left,Width=365,Padding=new Padding(18),AutoScroll=true}; Controls.Add(web); Controls.Add(panel); web.Dock=DockStyle.Fill;
  var layout=new FlowLayoutPanel {Dock=DockStyle.Fill,FlowDirection=FlowDirection.TopDown,WrapContents=false,AutoScroll=true}; panel.Controls.Add(layout);
  Action<Control> add=c=>{c.Margin=new Padding(0,0,0,9); layout.Controls.Add(c);};
  add(new Label{Text="Find a delivery location",Font=new Font("Segoe UI",17,FontStyle.Bold),AutoSize=true});
  add(new Label{Text="LG monitor · India · 10 background workers",AutoSize=true,ForeColor=Color.DimGray});
  add(new Label{Text="Amazon product URL",AutoSize=true}); url.Text=DefaultUrl; url.Width=310; add(url);
  open=Button("Open product",async delegate {try {await Navigate(url.Text);} catch(Exception ex){Notice(ex.Message);}});add(open);
  add(new Label{Text="Reference price (₹) / allowed increase (%)",AutoSize=true});
  var row=new FlowLayoutPanel {Width=310,Height=35}; baseline.Maximum=10000000;baseline.DecimalPlaces=2;baseline.Value=3119;baseline.Width=150;tolerance.Maximum=100;tolerance.DecimalPlaces=1;tolerance.Value=5;tolerance.Width=130;row.Controls.Add(baseline);row.Controls.Add(tolerance);add(row);
  add(new Label{Text="Limit includes displayed delivery charge.\nCoupons, EMI and checkout changes are excluded.",AutoSize=true,ForeColor=Color.DimGray});
  load=Button("Load pincode TXT or CSV",delegate {LoadPins();}); add(load);
  pins.Multiline=true;pins.MaxLength=2000000;pins.ScrollBars=ScrollBars.Vertical;pins.Width=310;pins.Height=85;pins.Text="110001\r\n201304\r\n400001\r\n560001\r\n600001\r\n700001\r\n500001\r\n411001\r\n380001\r\n302001";add(pins);
  add(new Label{Text="Seconds between checks per worker (min 3)",AutoSize=true});delay.Minimum=3;delay.Maximum=600;delay.Value=3;delay.Width=310;add(delay);
  first.Text="Pause after first matching pincode";first.Checked=false;first.Width=310;add(first);
  start=Button("Start new check",async delegate {await StartNew();});start.BackColor=Color.FromArgb(255,216,20);add(start);
  var buttons=new FlowLayoutPanel{Width=310,Height=40}; stop=Button("Pause",delegate {Pause("Paused. Results saved; Resume retries unfinished pincodes.");});stop.Width=80;resume=Button("Resume",async delegate {await Resume();});resume.Width=90;skip=Button("Skip / review",delegate {Skip();});skip.Width=108;buttons.Controls.Add(stop);buttons.Controls.Add(resume);buttons.Controls.Add(skip);add(buttons);
  var workerNumber=new NumericUpDown {Minimum=1,Maximum=WorkerCount,Value=1,Width=80};add(workerNumber);
  add(Button("Show selected worker",delegate {if(workers!=null)workers[(int)workerNumber.Value-1].Show();}));
  add(Button("Open saved results",delegate {System.Diagnostics.Process.Start("explorer.exe",session==null?Path.Combine(root,"results"):session.folder);}));
  status.Text="Open the product, then start a check.";status.Width=310;status.Height=58;status.ForeColor=Color.FromArgb(25,75,105);add(status);counts.Width=310;counts.Height=25;add(counts);
  log.Multiline=true;log.ReadOnly=true;log.ScrollBars=ScrollBars.Vertical;log.Width=310;log.Height=130;add(log);
  web.NavigationStarting+=(s,e)=>{navigations++;};
  Shown+=async(s,e)=>{await Initialize();};
  FormClosing+=(s,e)=>{if(running){Pause("Pausing. Close the window again once the check has stopped.");e.Cancel=true;}else if(workers!=null)foreach(var worker in workers)worker.Close();};
 }
 Button Button(string text,EventHandler action) {var b=new Button{Text=text,Width=310,Height=34,FlatStyle=FlatStyle.Flat};b.Click+=action;return b;}
 void Notice(string message) {status.Text=message;log.AppendText(DateTime.Now.ToString("HH:mm:ss")+"  "+message+Environment.NewLine);}
 async Task Initialize() {
  try {
   var env=await CoreWebView2Environment.CreateAsync(null,Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData),"AmazonPincodeChecker","Browser"));
   await web.EnsureCoreWebView2Async(env);browserReady=true;
   web.CoreWebView2.NewWindowRequested+=(s,e)=>{e.Handled=true;Notice("A new-window link was blocked. Use the product page in this window.");};
   if(mode=="--self-test") {await SelfTest();Close();return;}
   await Navigate(DefaultUrl);
   if(mode=="--probe") {await Task.Delay(7000);var p=await Read("probe");File.WriteAllText(Path.Combine(root,"results","live-probe.json"),json.Serialize(p));Close();return;}
   if(mode=="--sample") {await Task.Delay(5000);await StartNew();Close();return;}
   Notice("Ready. Confirm the product in the browser; then start.");
  } catch(Exception ex) {Notice("Browser startup: "+ex.Message);File.WriteAllText(Path.Combine(root,"results","startup-error.txt"),ex.ToString());}
 }
 async Task Navigate(string target) {
  if(!browserReady) throw new Exception("Browser is still starting.");
  if(!Rules.Amazon(target)||Rules.Asin(target)=="") throw new Exception("Enter an https://www.amazon.in/dp/ASIN product URL.");
  var done=new TaskCompletionSource<bool>(); EventHandler<CoreWebView2NavigationCompletedEventArgs> handler=null;
  handler=(s,e)=>{done.TrySetResult(e.IsSuccess);};web.NavigationCompleted+=handler;
  try {web.CoreWebView2.Navigate(target);if(await Task.WhenAny(done.Task,Task.Delay(45000))!=done.Task || !await done.Task) throw new Exception("Product page did not load. Check your connection and retry.");await Task.Delay(1500);}
  finally {web.NavigationCompleted-=handler;}
 }
 async Task<string> Script(string action,string pin) {
  string source=File.ReadAllText(Path.Combine(root,"src","page.js"));
  return await web.ExecuteScriptAsync(source.Replace("ACTION",json.Serialize(action)).Replace("PIN",json.Serialize(pin??"")));
 }
 async Task<Snapshot> Read(string action="read") {return json.Deserialize<Snapshot>(await Script(action,""))??new Snapshot();}
 async Task<bool> Act(string action,string pin) {var d=json.Deserialize<Dictionary<string,object>>(await Script(action,pin));return d!=null && d.ContainsKey("ok") && (bool)d["ok"];}
 void LoadPins() {
  using(var d=new OpenFileDialog{InitialDirectory=Path.Combine(root,"data"),Filter="Pincode files|*.txt;*.csv;*.tsv|All files|*.*"}) if(d.ShowDialog()==DialogResult.OK) {
   try {var values=ParsePins(d.FileName);pins.Text=String.Join(Environment.NewLine,values);Notice(values.Count+" unique pincodes loaded.");}catch(Exception ex){Notice(ex.Message);}
  }
 }
 static List<string> ParsePins(string path) {
  var lines=File.ReadAllLines(path);var values=new List<string>();
  // Only the pincode column in a CSV is used; unrelated numeric fields are not postal codes.
  if(path.EndsWith(".csv",StringComparison.OrdinalIgnoreCase)) {
   var header=CsvLine(lines.Length>0?lines[0]:"");int col=header.FindIndex(x=>Regex.IsMatch(x.Trim(),@"^(?:pin[ _-]?code|postal[ _-]?code|pin)$",RegexOptions.IgnoreCase));
   if(col<0) throw new Exception("CSV must have a pincode or postal_code column. TXT can contain one code per line.");
   foreach(var line in lines.Skip(1)){var cells=CsvLine(line);if(cells.Count>col && Regex.IsMatch(cells[col].Trim(),@"^[1-9]\d{5}$"))values.Add(cells[col].Trim());}
  } else foreach(Match m in Regex.Matches(String.Join("\n",lines),@"(?<!\d)[1-9]\d{5}(?!\d)"))values.Add(m.Value);
  return values.Distinct().ToList();
 }
 static List<string> CsvLine(string line) {var a=new List<string>();var b=new StringBuilder();bool quoted=false;for(int i=0;i<line.Length;i++){char c=line[i];if(c=='"'){if(quoted&&i+1<line.Length&&line[i+1]=='"'){b.Append('"');i++;}else quoted=!quoted;}else if(c==','&&!quoted){a.Add(b.ToString());b.Clear();}else b.Append(c);}a.Add(b.ToString());return a;}
 async Task StartNew() {
  if(running)return;
  if(!Rules.Amazon(url.Text)||Rules.Asin(url.Text)==""){Notice("Enter a valid Amazon.in product URL.");return;}
  var list=Regex.Matches(pins.Text,@"(?<!\d)[1-9]\d{5}(?!\d)").Cast<Match>().Select(m=>m.Value).Distinct().ToList();
  if(list.Count==0){Notice("Load or paste at least one six-digit pincode.");return;}
  session=new Session{version=2,url=url.Text,baseline=baseline.Value,tolerance=tolerance.Value,delay=(int)delay.Value,pins=list,folder=Path.Combine(root,"results",DateTime.Now.ToString("yyyyMMdd-HHmmss-fff"))};
  Directory.CreateDirectory(session.folder);Save();await Run();
 }
 async Task Resume() {
  if(running)return;
  try {if(session==null){string pointer=Path.Combine(root,"results","last-session.txt");if(!File.Exists(pointer))throw new Exception("No previous check to resume.");session=json.Deserialize<Session>(File.ReadAllText(File.ReadAllText(pointer)));}
   session.Upgrade();
   // Resume keeps the saved product/price settings but honors the currently selected delay.
   session.delay=(int)delay.Value;url.Text=session.url;baseline.Value=session.baseline;tolerance.Value=session.tolerance;await Run();
  }catch(Exception ex){Notice(ex.Message);}
 }
 void ControlsRunning(bool value) {start.Enabled=resume.Enabled=load.Enabled=open.Enabled=pins.Enabled=url.Enabled=baseline.Enabled=tolerance.Enabled=delay.Enabled=skip.Enabled=!value;stop.Enabled=value;}
 async Task Run() {
  running=true;stopping=false;pauseReason=null;reviewIndex=-1;ControlsRunning(true);
  runCancellation=new CancellationTokenSource();
  try {
   RecoverResults();
   if(workers==null)workers=Enumerable.Range(1,WorkerCount).Select(n=>new BrowserWorker(n,root)).ToArray();
   var pending=new WorkQueue(session.Pending());
   // Each worker starts independently: a failed browser does not stop its peers.
   await Task.WhenAll(workers.Select((w,i)=>WorkerLoop(w,i+1,pending,runCancellation.Token)));
   Save();Notice(session.completed.Count==session.pins.Count?"Finished. "+session.good_pincodes.Count+" matching pincodes saved.":pauseReason??"Run ended. Unresolved pincodes remain; Resume retries them. See the log.");
  }catch(OperationCanceledException){Save();Notice(pauseReason??"Paused.");}
  catch(Exception ex){Notice("Paused: "+ex.Message);try{Save();}catch(Exception saveEx){Notice("Could not save: "+saveEx.Message);}}
  finally {running=false;runCancellation.Dispose();runCancellation=null;ControlsRunning(false);}
 }
 void Pause(string reason) {if(!stopping){pauseReason=reason;stopping=true;}if(runCancellation!=null)runCancellation.Cancel();Notice(pauseReason??reason);}
 async Task WorkerLoop(BrowserWorker worker,int number,WorkQueue pending,CancellationToken token) {
  try {
   var startup=worker.Start();
   var winner=await Task.WhenAny(startup,Task.Delay(90000,token));token.ThrowIfCancellationRequested();
   if(winner!=startup)throw new Exception("Browser startup timed out.");await startup;
  }catch(OperationCanceledException){return;}
  catch(Exception ex){Notice("Worker "+number+" unavailable: "+ex.Message);worker.Close();workers[number-1]=new BrowserWorker(number,root);return;}
  int index;
  while(!token.IsCancellationRequested&&pending.TryClaim(out index)) {
   string pin=session.pins[index];bool needsHuman=false;
   try {
    CheckResult result=null;
    for(int attempt=1;attempt<=3;attempt++) {
     try {
      Notice("Worker "+number+": "+pin+" (attempt "+attempt+"/3)");
      result=await worker.Check(session.url,pin,session.baseline*(1+session.tolerance/100),token);break;
     }catch(PageLoadException ex) {
      if(attempt==3)throw;
      Notice("Worker "+number+", "+pin+": "+ex.Message+" Retrying the same pincode…");
     }
     await Task.Delay(TimeSpan.FromSeconds(Math.Max(3,session.delay)),token);
    }
    token.ThrowIfCancellationRequested();
    if(result.verdict.status=="REVIEW") {
     Record(pin,result.verdict,result.snapshot);reviewIndex=index;
     Notice("Worker "+number+", "+pin+": review needed. Other workers continue.");
     needsHuman=result.snapshot.blocked;
    } else {
     CommitResult(index,result);Notice("Worker "+number+": "+pin+" — "+result.verdict.status);
     if(result.verdict.status=="GOOD"&&first.Checked){Pause("Match found: "+pin+". Saved; Resume continues unfinished pincodes.");return;}
    }
   }catch(OperationCanceledException){return;}
   catch(IOException ex){Pause("Saving failed: "+ex.Message+". Check free space and Resume.");return;}
   catch(Exception ex) {
    if(token.IsCancellationRequested)return;
    Record(pin,new Verdict("RETRY_LATER",ex.Message,null,null),null);
    Notice("Worker "+number+", "+pin+": "+ex.Message+" Deferred; other pincodes continue.");
    needsHuman=ex.Message.Contains("Human verification");
   }finally {pending.Retire(index);}
   // A challenged browser is parked, never routed through another profile to solve that challenge.
   if(needsHuman){Notice("Worker "+number+" needs human verification. Show this worker; Resume after resolving it.");return;}
   try{await Task.Delay(TimeSpan.FromSeconds(Math.Max(3,session.delay)),token);}catch(OperationCanceledException){return;}
  }
 }
 void CommitResult(int index,CheckResult result) {
  // Durable per-index result is the commit record. Repeating the commit is a no-op.
  string directory=Path.Combine(session.folder,"committed");Directory.CreateDirectory(directory);
  string path=Path.Combine(directory,index.ToString("D6")+".json");
  if(!File.Exists(path))Atomic(path,json.Serialize(result));
  else result=json.Deserialize<CheckResult>(File.ReadAllText(path));
  if(result.verdict.status=="GOOD"&&!session.good_pincodes.Contains(session.pins[index]))session.good_pincodes.Add(session.pins[index]);
  if(!session.completed.Contains(index)){Record(session.pins[index],result.verdict,result.snapshot);session.Complete(index);}
  Save();
 }
 void RecoverResults() {
  string directory=Path.Combine(session.folder,"committed");if(!Directory.Exists(directory))return;
  foreach(string path in Directory.GetFiles(directory,"*.json")) {
   int index;if(!int.TryParse(Path.GetFileNameWithoutExtension(path),out index)||index<0||index>=session.pins.Count)continue;
   var result=json.Deserialize<CheckResult>(File.ReadAllText(path));
   if(result.verdict.status=="GOOD"&&!session.good_pincodes.Contains(session.pins[index]))session.good_pincodes.Add(session.pins[index]);
   session.Complete(index);
  }
  Save();
 }
 void Skip(){if(running||session==null)return;int index=reviewIndex>=0?reviewIndex:session.next;if(index>=session.pins.Count||session.completed.Contains(index))return;Record(session.pins[index],new Verdict("SKIPPED","User skipped an unresolved pincode.",null,null),null);session.Complete(index);reviewIndex=-1;Save();Notice("Skipped. Resume to continue.");}
 static string Cell(object v){return "\""+Convert.ToString(v,CultureInfo.InvariantCulture).Replace("\"","\"\"")+"\"";}
 void Record(string pin,Verdict v,Snapshot s) {
  string path=Path.Combine(session.folder,"checks.csv");if(!File.Exists(path))File.WriteAllText(path,"time,pincode,status,item_price,shipping,reason,title,location,delivery,seller_and_offer\r\n",Encoding.UTF8);
  File.AppendAllText(path,String.Join(",",new object[]{DateTimeOffset.Now.ToString("o"),pin,v.status,v.price,v.shipping,v.reason,s==null?"":s.title,s==null?"":s.location,s==null?"":s.delivery,s==null?"":s.buybox}.Select(Cell))+"\r\n",Encoding.UTF8);
 }
 static void Atomic(string path,string contents){string temp=path+".tmp";File.WriteAllText(temp,contents,new UTF8Encoding(false));if(File.Exists(path))File.Replace(temp,path,null);else File.Move(temp,path);}
 void Save() {
  if(session==null)return;
  session.RefreshNext();
  string text=String.Join(Environment.NewLine,session.good_pincodes);if(text.Length>0)text+=Environment.NewLine;
  Atomic(Path.Combine(session.folder,"checkpoint.json"),json.Serialize(session));
  Atomic(Path.Combine(session.folder,"good_pincodes.txt"),text);Atomic(Path.Combine(root,"good_pincodes.txt"),text);
  Atomic(Path.Combine(session.folder,"unfinished_pincodes.txt"),String.Join(Environment.NewLine,session.Pending().Select(i=>session.pins[i])));
  Atomic(Path.Combine(root,"results","last-session.txt"),Path.Combine(session.folder,"checkpoint.json"));
  counts.Text=session.completed.Count+" / "+session.pins.Count+" checked · "+session.good_pincodes.Count+" matches";
 }
 async Task SelfTest() {
  var results=new List<string>();
  Action<bool,string> check=(ok,name)=>{if(!ok)throw new Exception("TEST FAILED: "+name);results.Add("PASS "+name);};
  var progress=new Session {version=2,pins=new List<string>{"110001","400001","560001","600001"}};
  var queue=progress.Pending();var claims=new[]{queue.Dequeue(),queue.Dequeue(),queue.Dequeue()};
  check(claims.Distinct().Count()==3,"three workers receive different queue entries");
  progress.Complete(2);progress.Complete(0);progress.Complete(2);
  check(progress.completed.Count==2&&progress.next==1,"out-of-order completion is retained without duplicate counts");
  var restored=json.Deserialize<Session>(json.Serialize(progress));restored.Upgrade();
  check(restored.Pending().SequenceEqual(new[]{1,3}),"checkpoint retries interrupted work and excludes completed work");
  var legacy=new Session {next=2,pins=progress.pins};legacy.Upgrade();
  check(legacy.Pending().SequenceEqual(new[]{2,3}),"single-worker checkpoints migrate without losing progress");
  var s=new Snapshot{href=DefaultUrl,title="LG monitor",location="New Delhi 110001",delivery="FREE delivery Monday, 19 October",price="₹3,119.00",cart=true,availability="In stock",buybox=""};
  check(Rules.Evaluate(s,"110001","B0B5V5VHB6",3275).status=="GOOD","explicit delivery and price accepted");
  check(Rules.Evaluate(s,"400001","B0B5V5VHB6",3275).status=="REVIEW","stale location rejected");
  s.buybox="This item cannot be shipped to your selected delivery location.";check(Rules.Evaluate(s,"110001","B0B5V5VHB6",3275).status=="NO_DELIVERY","negative delivery overrides positive text");s.buybox="";
  s.price="₹23,119.00";check(Rules.Evaluate(s,"110001","B0B5V5VHB6",3275).status=="PRICE_TOO_HIGH","price ceiling enforced");s.price="₹3,119.00";
  s.delivery="₹200 delivery Monday, 19 October";check(Rules.Evaluate(s,"110001","B0B5V5VHB6",3275).status=="PRICE_TOO_HIGH","shipping included in ceiling");
  s.delivery="FREE delivery on orders over ₹499 Monday, 19 October";check(Rules.Evaluate(s,"110001","B0B5V5VHB6",3275).status=="REVIEW","conditional shipping not assumed free");
  s.delivery="Delivery Monday, 19 October";check(Rules.Evaluate(s,"110001","B0B5V5VHB6",3275).status=="REVIEW","unknown shipping not accepted");
  s.blocked=true;check(Rules.Evaluate(s,"110001","B0B5V5VHB6",3275).status=="REVIEW","CAPTCHA pauses");s.blocked=false;
  s.href="https://example.com/dp/B0B5V5VHB6";check(Rules.Evaluate(s,"110001","B0B5V5VHB6",3275).status=="REVIEW","wrong host rejected");
  check(Rules.Money("₹1,23,456.78")==123456.78m,"Indian currency parsing");
  web.NavigateToString("<html><body><span id='productTitle'>LG monitor</span><span id='glow-ingress-line2'>Delhi 110001</span><div id='deliveryBlockMessage'>FREE delivery Monday, 19 October</div><div id='corePriceDisplay_desktop_feature_div'><span class='a-price'><span class='a-offscreen'>₹3,119.00</span></span></div><input id='add-to-cart-button' type='button' value='Add to cart'><a id='nav-global-location-popover-link' onclick='window.opened=true'>Delhi 110001</a><input id='GLUXZipUpdateInput'><span id='GLUXZipUpdate'><input type='button' onclick='window.applied=document.getElementById(\"GLUXZipUpdateInput\").value'></span></body></html>");
  await Task.Delay(1500);var sample=await Read();check(sample.price=="₹3,119.00"&&sample.cart&&sample.location.Contains("110001"),"real WebView2 DOM extraction");
  check(await Act("open","400001"),"location control click");check(await Act("apply","400001"),"pincode apply control");
  check((await web.ExecuteScriptAsync("window.applied"))=="\"400001\"","native input event sets requested code");
  string tmp=Path.Combine(root,"results","test-pins.csv");File.WriteAllText(tmp,"pincode,number\n110001,999999\n110001,123456\n400001,234567\n000000,555555");check(ParsePins(tmp).SequenceEqual(new[]{"110001","400001"}),"CSV column filtering and deduplication");
  string output=Path.Combine(root,"results","test-atomic.txt");Atomic(output,"110001\n");Atomic(output,"400001\n");check(File.ReadAllText(output)=="400001\n","atomic result replacement");
  File.WriteAllLines(Path.Combine(root,"results","self-test.txt"),results);
 }
 [STAThread] public static void Main(string[] args) {Application.EnableVisualStyles();Application.SetCompatibleTextRenderingDefault(false);
 if(System.Diagnostics.Process.GetProcessesByName("AmazonPincodeChecker").Any(p=>p.Id!=System.Diagnostics.Process.GetCurrentProcess().Id)){MessageBox.Show("Pause and close the older Amazon delivery finder before opening this update. Its progress is saved for Resume.");return;}
 bool owns;using(var mutex=new Mutex(true,"Local\\AmazonPincodeChecker.ScanOwner",out owns)) {
  if(!owns){MessageBox.Show("Amazon delivery finder is already open. Use that window to avoid duplicate work.");return;}
  try{Application.Run(new App(args.Length>0?args[0]:""));}finally{mutex.ReleaseMutex();}
 }}
}
