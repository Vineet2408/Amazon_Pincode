using System;
using System.IO;
using System.Collections.Generic;
using System.Threading;
using System.Threading.Tasks;
using System.Web.Script.Serialization;
using System.Windows.Forms;
using Microsoft.Web.WebView2.Core;
using Microsoft.Web.WebView2.WinForms;

public sealed class CheckResult {
 public Snapshot snapshot;
 public Verdict verdict;
}
public sealed class PageLoadException : Exception {public PageLoadException(string message):base(message){}}

// Each WebView2 owns a separate STA thread, message pump and cookie store.
// Only immutable check inputs/results cross threads. Session/file writes stay on the main UI thread.
public sealed class BrowserWorker {
 readonly int number;
 readonly string root;
 readonly JavaScriptSerializer json=new JavaScriptSerializer();
 readonly TaskCompletionSource<bool> ready=new TaskCompletionSource<bool>(TaskCreationOptions.RunContinuationsAsynchronously);
 Form form;
 WebView2 browser;
 Thread thread;
 int navigations;
 volatile bool closing;
 public BrowserWorker(int number,string root) {this.number=number;this.root=root;}
 public Task Start() {
  if(thread!=null)return ready.Task;
  thread=new Thread(delegate() {
   try {
    if(closing){ready.TrySetCanceled();return;}
    form=new Form {Text="Amazon delivery worker "+number,Width=1100,Height=800,ShowInTaskbar=false,Opacity=0};
    browser=new WebView2 {Dock=DockStyle.Fill};form.Controls.Add(browser);
    browser.NavigationStarting+=(s,e)=>{navigations++;};
    form.FormClosing+=(s,e)=>{if(!closing){e.Cancel=true;form.Hide();}};
    form.Shown+=async(s,e)=>{
     try {
      if(closing){ready.TrySetCanceled();form.Close();return;}
      var profile=Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData),"AmazonPincodeChecker","Worker"+number);
      var options=new CoreWebView2EnvironmentOptions("--disable-background-timer-throttling --disable-renderer-backgrounding --disable-backgrounding-occluded-windows");
      // GPU acceleration remains enabled by WebView2's normal hardware capability detection.
      var env=await CoreWebView2Environment.CreateAsync(null,profile,options);
      await browser.EnsureCoreWebView2Async(env);
      browser.CoreWebView2.NewWindowRequested+=(sender,args)=>{args.Handled=true;};
      ready.TrySetResult(true);
      form.Hide();form.Opacity=1;
     }catch(Exception ex){ready.TrySetException(ex);}
    };
    Application.Run(form);
   }catch(Exception ex){ready.TrySetException(ex);}
  });
  thread.Name="Amazon pincode worker "+number;thread.IsBackground=true;
  thread.SetApartmentState(ApartmentState.STA);thread.Start();return ready.Task;
 }
 public void Show() {if(form!=null&&!form.IsDisposed)form.BeginInvoke(new Action(()=>{form.Opacity=1;form.ShowInTaskbar=true;form.Show();form.WindowState=FormWindowState.Normal;form.Activate();}));}
 public void Close() {
  closing=true;
  try {if(form!=null&&!form.IsDisposed&&form.IsHandleCreated)form.BeginInvoke(new Action(()=>form.Close()));}
  catch(InvalidOperationException) { /* Message loop has already exited. */ }
 }
 public async Task<CheckResult> Check(string url,string pin,decimal maximum,CancellationToken token) {
  await ready.Task;token.ThrowIfCancellationRequested();
  var done=new TaskCompletionSource<CheckResult>(TaskCreationOptions.RunContinuationsAsynchronously);
  form.BeginInvoke(new Action(async()=>{
   try {form.Text="Worker "+number+" — checking "+pin;done.TrySetResult(await CheckOnBrowser(url,pin,maximum,token));}
   catch(OperationCanceledException){done.TrySetCanceled();}
   catch(Exception ex){done.TrySetException(ex);}
  }));
  return await done.Task;
 }
 async Task Navigate(string url,CancellationToken token) {
  token.ThrowIfCancellationRequested();
  var done=new TaskCompletionSource<bool>();
  ulong? navigationId=null;
  EventHandler<CoreWebView2NavigationStartingEventArgs> starting=(s,e)=>{if(!navigationId.HasValue)navigationId=e.NavigationId;};
  EventHandler<CoreWebView2NavigationCompletedEventArgs> handler=(s,e)=>{if(navigationId.HasValue&&navigationId.Value==e.NavigationId)done.TrySetResult(e.IsSuccess);};
  browser.NavigationStarting+=starting;
  browser.NavigationCompleted+=handler;
  try {
   browser.CoreWebView2.Navigate(url);
   var winner=await Task.WhenAny(done.Task,Task.Delay(45000,token));token.ThrowIfCancellationRequested();
   if(winner!=done.Task||!await done.Task)throw new PageLoadException("Product page did not load.");
   await Task.Delay(1500,token);
  }finally {browser.NavigationStarting-=starting;browser.NavigationCompleted-=handler;browser.CoreWebView2.Stop();}
 }
 async Task<string> Script(string action,string pin,CancellationToken token) {
  token.ThrowIfCancellationRequested();
  string source=File.ReadAllText(Path.Combine(root,"src","page.js"));
  return await browser.ExecuteScriptAsync(source.Replace("ACTION",json.Serialize(action)).Replace("PIN",json.Serialize(pin??"")));
 }
 async Task<Snapshot> Read(CancellationToken token) {return json.Deserialize<Snapshot>(await Script("read","",token))??new Snapshot();}
 async Task<bool> Act(string action,string pin,CancellationToken token) {
  var d=json.Deserialize<Dictionary<string,object>>(await Script(action,pin,token));return d!=null&&d.ContainsKey("ok")&&(bool)d["ok"];
 }
 async Task<CheckResult> CheckOnBrowser(string url,string pin,decimal maximum,CancellationToken token) {
  await Navigate(url,token);
  var before=await Read(token);
  if(before.blocked)throw new Exception("Human verification required. Resolve it in this worker window, then Resume.");
  if(!await Act("open",pin,token))throw new Exception("Delivery-location control not found.");
  bool applied=false;
  for(int i=0;i<12;i++){await Task.Delay(500,token);if(await Act("apply",pin,token)){applied=true;break;}}
  if(!applied)throw new Exception("Pincode input was not available.");
  await Task.Delay(2500,token);var after=await Read(token);
  if(!String.IsNullOrWhiteSpace(after.invalid))return new CheckResult{snapshot=after,verdict=new Verdict("INVALID_PIN",after.invalid,null,null)};
  await Act("close",pin,token);await Task.Delay(1200,token);
  await Navigate(url,token);await Task.Delay(2500,token);
  int epoch=navigations;var a=await Read(token);await Task.Delay(1500,token);var b=await Read(token);
  if(epoch!=navigations||!a.ready||!b.ready||a.location!=b.location||a.price!=b.price||a.delivery!=b.delivery||a.cart!=b.cart||a.availability!=b.availability||a.buybox!=b.buybox)
   throw new PageLoadException("Page is still changing.");
  token.ThrowIfCancellationRequested();
  return new CheckResult {snapshot=b,verdict=Rules.Evaluate(b,pin,Rules.Asin(url),maximum)};
 }
}
