using System;
using System.IO;
using System.Linq;
using System.Collections.Generic;
using System.Diagnostics;
using System.Drawing;
using System.Net;
using System.Net.NetworkInformation;
using System.Net.Security;
using System.Net.Sockets;
using System.Runtime.InteropServices;
using System.Threading;
using System.Threading.Tasks;
using System.Windows.Forms;
using System.Xml.Serialization;

[assembly: System.Reflection.AssemblyTitle("Claude VPN Guard")]
[assembly: System.Reflection.AssemblyDescription("VPN-aware launcher and watchdog for Claude Desktop")]
[assembly: System.Reflection.AssemblyVersion("0.2.0.0")]
[assembly: System.Reflection.AssemblyFileVersion("0.2.0.0")]
[assembly: System.Reflection.AssemblyCopyright("Copyright (c) 2026 MrFerrumL. MIT License.")]

public class Settings {
    public string Adapter = "v2RayTun;MantaRay";
    public bool AutoDetectTunnel = true;
    public int GraceSeconds = 10;
    public string AppId = "Claude_pzs8sxrjxfjjc!Claude";
}
public sealed class Result {
    public bool Good;
    public string Reason;
    public long Started;
    public string AdapterId;
    public Result(bool good, string reason, long started, string adapterId = "") { Good=good; Reason=reason; Started=started; AdapterId=adapterId; }
}
public sealed class Policy {
    readonly HashSet<int> admitted = new HashSet<int>();
    public long? BadSince;
    public void Update(bool good, long now) { if(good) BadSince=null; else if(!BadSince.HasValue) BadSince=now; }
    public bool Keep(int id, bool good, long now, int grace) {
        if(good) { admitted.Add(id); return true; }
        return admitted.Contains(id) && BadSince.HasValue && now-BadSince.Value < grace*1000L;
    }
    public void Prune(IEnumerable<int> ids) { admitted.IntersectWith(ids); }
}
static class Program {
    public static readonly string Root=AppDomain.CurrentDomain.BaseDirectory;
    public static readonly string State=Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData), "ClaudeVpnGuard");
    public static readonly Stopwatch Clock=Stopwatch.StartNew();
    public static long Now { get { return Clock.ElapsedMilliseconds; } }
    public static void Log(string message) { try { Directory.CreateDirectory(State); string p=Path.Combine(State,"guard.log"); if(File.Exists(p)&&new FileInfo(p).Length>1048576) File.WriteAllText(p,""); File.AppendAllText(p,DateTimeOffset.Now.ToString("o")+" "+message+Environment.NewLine); } catch {} }
    public static Settings Load() { try { using(var f=File.OpenRead(Path.Combine(Root,"settings.xml"))) { var s=(Settings)new XmlSerializer(typeof(Settings)).Deserialize(f); s.GraceSeconds=Math.Max(5,Math.Min(10,s.GraceSeconds)); return s; } } catch { return new Settings(); } }
    public static void Save(Settings s) { using(var f=File.Create(Path.Combine(Root,"settings.xml"))) new XmlSerializer(typeof(Settings)).Serialize(f,s); }
    [STAThread] static void Main(string[] args) {
        Application.EnableVisualStyles();
        if(args.Length>0 && args[0]=="--self-test") { string path=args.Length>1?args[1]:Path.Combine(Root,"tests.txt"); try { SelfTest(path); } catch(Exception e) { File.WriteAllText(path,"FAIL: "+e.Message); Environment.ExitCode=1; } return; }
        if(args.Length>0 && args[0]=="--diagnose") { if(args.Length<2) { Environment.ExitCode=2; return; } var r=Probe.Check(Load()).GetAwaiter().GetResult(); File.WriteAllText(args[1],"VPN="+r.Good+Environment.NewLine+r.Reason); return; }
        bool fresh;
        using(var mutex=new Mutex(true,"Local\\ClaudeVpnGuard-"+Environment.UserName,out fresh)) {
            if(!fresh) { if(args.Contains("--launch")) try { using(var ev=EventWaitHandle.OpenExisting("Local\\ClaudeVpnGuardLaunch-"+Environment.UserName)) ev.Set(); } catch {} return; }
            try { Application.Run(new Guard(args.Contains("--launch"))); }
            catch(Exception e) { Log(e.ToString()); MessageBox.Show("Защита Claude остановлена: "+e.Message,"Claude VPN Guard"); }
        }
    }
    static void Assert(bool condition,string name) { if(!condition) throw new Exception(name); }
    static void SelfTest(string path) {
        var p=new Policy(); p.Update(false,0); Assert(!p.Keep(1,false,0,10),"new process without VPN");
        p.Update(true,100); Assert(p.Keep(1,true,100,10),"healthy launch");
        p.Update(false,200); Assert(p.Keep(1,false,10199,10),"grace before deadline"); Assert(!p.Keep(1,false,10200,10),"close at ten seconds");
        Assert(!p.Keep(2,false,300,10),"new launch during outage denied");
        p.Update(true,500); p.Keep(1,true,500,10); p.Update(false,1000); Assert(p.Keep(1,false,10999,10),"recovery resets deadline");
        Assert(!p.Keep(1,false,6000,5),"five seconds option"); p.Prune(new int[0]); Assert(!p.Keep(1,false,1100,10),"pid reuse is not admitted");
        var s=new Settings();
        Assert(Probe.IsCandidate(s,"v2RayTun","sing-tun Tunnel"),"v2RayTun accepted");
        Assert(Probe.IsCandidate(s,"MantaRay","MantaRay TUN"),"MantaRay accepted");
        Assert(Probe.IsCandidate(s,"Ethernet 2","TAP-Windows Adapter V9"),"MantaRay/OpenVPN TAP driver accepted");
        Assert(!Probe.IsCandidate(s,"Wi-Fi","MediaTek Wi-Fi 6E"),"physical Wi-Fi rejected");
        Assert(!Probe.IsCandidate(s,"Ethernet 3","VirtualBox Host-Only Ethernet Adapter"),"host-only adapter rejected");
        s.AutoDetectTunnel=false; Assert(!Probe.IsCandidate(s,"Unknown","sing-tun Tunnel"),"automatic detection can be disabled");
        Assert(Probe.IsCandidate(s,"MantaRay","Custom Driver"),"explicit adapter accepted without automatic detection");
        s.Adapter="Custom VPN; Other"; Assert(Probe.IsCandidate(s,"Other","Custom Driver"),"multiple explicit adapters");
        File.WriteAllText(path,"PASS: 16 checks (8 policy, 8 adapter selection). No applications were stopped and VPN was not changed.");
    }
}
static class Probe {
    [DllImport("iphlpapi.dll")] static extern uint GetBestInterface(uint address,out uint index);
    public static bool IsCandidate(Settings s,string name,string description) {
        if((s.Adapter??"").Split(';').Any(a=>String.Equals(a.Trim(),name,StringComparison.OrdinalIgnoreCase))) return true;
        if(!s.AutoDetectTunnel) return false;
        string d=(description??"").ToLowerInvariant();
        return d.Contains("sing-tun") || d.Contains("wintun") || d.Contains("wireguard") || d.Contains("tap-windows") || d.Contains("mantaray");
    }
    public static NetworkInterface Adapter(Settings s) { return NetworkInterface.GetAllNetworkInterfaces().FirstOrDefault(n=>IsCandidate(s,n.Name,n.Description)&&n.OperationalStatus==OperationalStatus.Up&&Routed(n,IPAddress.Parse("1.1.1.1"))); }
    static bool Routed(NetworkInterface n,IPAddress ip) { try { uint i; return GetBestInterface(BitConverter.ToUInt32(ip.GetAddressBytes(),0),out i)==0 && i==(uint)n.GetIPProperties().GetIPv4Properties().Index; } catch { return false; } }
    public static bool Structural(Settings s,string expectedId=null) { try { var n=Adapter(s); return n!=null && (expectedId==null || n.Id==expectedId); } catch { return false; } }
    static async Task Deadline(Task task,int ms) { if(await Task.WhenAny(task,Task.Delay(ms))!=task) throw new TimeoutException("Истекло время проверки соединения"); await task; }
    public static async Task<Result> Check(Settings s) {
        long start=Program.Now;
        try {
            var n=Adapter(s); if(n==null) return new Result(false,"Нет разрешённого VPN-туннеля с интернет-маршрутом",start);
            if(!Routed(n,IPAddress.Parse("1.1.1.1"))) return new Result(false,"Интернет-маршрут идёт вне "+s.Adapter,start);
            var dns=Dns.GetHostAddressesAsync("claude.ai"); await Deadline(dns,1000);
            var ip=dns.Result.FirstOrDefault(a=>a.AddressFamily==AddressFamily.InterNetwork && Routed(n,a));
            if(ip==null) return new Result(false,"Маршрут Claude не проходит через VPN",start);
            var local=n.GetIPProperties().UnicastAddresses.FirstOrDefault(a=>a.Address.AddressFamily==AddressFamily.InterNetwork);
            if(local==null) return new Result(false,"У туннеля нет IPv4-адреса",start);
            using(var tcp=new TcpClient(new IPEndPoint(local.Address,0))) {
                await Deadline(tcp.ConnectAsync(ip,443),1000);
                using(var ssl=new SslStream(tcp.GetStream(),false)) {
                    await Deadline(ssl.AuthenticateAsClientAsync("claude.ai", new System.Security.Cryptography.X509Certificates.X509CertificateCollection(), System.Security.Authentication.SslProtocols.Tls12, false),1500);
                }
            }
            if(!Structural(s,n.Id)) return new Result(false,"Туннель сменился во время проверки",start);
            return new Result(true,"VPN работает: "+n.Name+"; TLS claude.ai проверен",start,n.Id);
        } catch(Exception e) { return new Result(false,"Соединение не подтверждено: "+e.GetBaseException().Message,start); }
    }
}
sealed class Guard : ApplicationContext {
    readonly Settings settings=Program.Load();
    readonly NotifyIcon tray=new NotifyIcon();
    readonly System.Windows.Forms.Timer timer=new System.Windows.Forms.Timer();
    readonly EventWaitHandle launchEvent=new EventWaitHandle(false,EventResetMode.AutoReset,"Local\\ClaudeVpnGuardLaunch-"+Environment.UserName);
    readonly Policy policy=new Policy();
    readonly ToolStripMenuItem status=new ToolStripMenuItem("Проверка VPN…");
    Task<Result> job;
    Result last;
    long nextProbe;
    bool launchPending;
    bool? previous;
    long lastNotice=-10000;
    public Guard(bool launch) {
        launchPending=launch;
        tray.Icon=SystemIcons.Shield; tray.Text="Claude VPN Guard: проверка";
        var menu=new ContextMenuStrip(); status.Enabled=false; menu.Items.Add(status);
        menu.Items.Add("Открыть Claude с проверкой VPN",null,(s,e)=>RequestLaunch());
        menu.Items.Add("Настройки",null,(s,e)=>Configure());
        menu.Items.Add("Открыть журнал",null,(s,e)=> { Program.Log("Журнал открыт"); Process.Start("notepad.exe",Path.Combine(Program.State,"guard.log")); });
        menu.Items.Add("Выйти и отключить защиту",null,(s,e)=> { if(MessageBox.Show("После выхода контроль VPN прекратится. Выйти?","Claude VPN Guard",MessageBoxButtons.YesNo,MessageBoxIcon.Question)==DialogResult.Yes) ExitThread(); });
        tray.ContextMenuStrip=menu; tray.DoubleClick+=(s,e)=>RequestLaunch(); tray.Visible=true;
        // Existing desktop sessions receive the same grace period as a connection loss.
        foreach(var p in DesktopProcesses()) { policy.Keep(p.Id,true,Program.Now,settings.GraceSeconds); p.Dispose(); }
        timer.Interval=250; timer.Tick+=(s,e)=>Tick(); timer.Start();
        Program.Log("Защита запущена; адаптеры="+settings.Adapter+"; автоопределение="+settings.AutoDetectTunnel+"; задержка="+settings.GraceSeconds);
        Tick();
    }
    static List<Process> DesktopProcesses() {
        var found=new List<Process>();
        foreach(var p in Process.GetProcessesByName("claude")) {
            try { string path=p.MainModule.FileName; if(path.EndsWith("\\app\\Claude.exe",StringComparison.OrdinalIgnoreCase)||path.EndsWith("\\Claude\\Claude.exe",StringComparison.OrdinalIgnoreCase)) { found.Add(p); continue; } }
            catch(Exception e) { Program.Log("Не удалось определить процесс "+p.Id+": "+e.Message); }
            p.Dispose();
        }
        return found;
    }
    void RequestLaunch() { launchPending=true; nextProbe=0; }
    void Tick() {
        if(launchEvent.WaitOne(0)) RequestLaunch();
        long now=Program.Now;
        if(job!=null && job.IsCompleted) { last=job.GetAwaiter().GetResult(); job=null; nextProbe=now+1500; }
        if(job==null && now>=nextProbe) job=Probe.Check(settings);
        bool structural=Probe.Structural(settings);
        bool good=structural && last!=null && last.Good && now-last.Started<5000 && Probe.Structural(settings,last.AdapterId);
        if(last==null && structural && now<4500) return;
        policy.Update(good,now);
        string reason=good?last.Reason:(!structural?"Туннель VPN выключен":last==null||last.Good?"Проверка текущего VPN-туннеля":last.Reason);
        status.Text=reason;
        tray.Text=good?"Claude VPN Guard: VPN работает":"Claude VPN Guard: VPN не подтверждён";
        tray.Icon=good?SystemIcons.Shield:SystemIcons.Warning;
        if(previous!=good) { Program.Log(reason); previous=good; }
        var ps=DesktopProcesses(); policy.Prune(ps.Select(p=>p.Id));
        foreach(var p in ps) {
            try { if(!policy.Keep(p.Id,good,now,settings.GraceSeconds)) { p.Kill(); Program.Log("Claude закрыт, PID="+p.Id+"; "+reason); Notice("Claude закрыт: VPN недоступен. "+settings.GraceSeconds+" с — допуск для уже открытого приложения."); } }
            catch(Exception e) { Program.Log("Не удалось закрыть Claude: "+e.Message); Notice("Не удалось закрыть Claude. Подробности в журнале."); }
            finally { p.Dispose(); }
        }
        if(launchPending && job==null) {
            launchPending=false;
            if(good) { try { Process.Start("explorer.exe","shell:AppsFolder\\"+settings.AppId); Program.Log("Запуск Claude после проверки VPN"); } catch(Exception e) { Notice("Не удалось открыть Claude: "+e.Message); } }
            else Notice("Запуск Claude заблокирован. Подключи VPN и повтори запуск.");
        }
    }
    void Notice(string message) { if(Program.Now-lastNotice<4000) return; lastNotice=Program.Now; tray.ShowBalloonTip(4000,"Claude VPN Guard",message,ToolTipIcon.Warning); }
    void Configure() {
        using(var f=new Form()) {
            f.Text="Настройки Claude VPN Guard"; f.ClientSize=new Size(460,240); f.FormBorderStyle=FormBorderStyle.FixedDialog; f.MaximizeBox=false; f.MinimizeBox=false; f.StartPosition=FormStartPosition.CenterScreen;
            var a=new ComboBox { Left=20,Top=40,Width=380,DropDownStyle=ComboBoxStyle.DropDown };
            a.Items.AddRange(NetworkInterface.GetAllNetworkInterfaces().Select(n=>(object)n.Name).ToArray()); a.Text=settings.Adapter;
            var delay=new NumericUpDown {Left=280,Top=85,Width=65,Minimum=5,Maximum=10,Value=settings.GraceSeconds};
            f.Controls.Add(new Label {Left=20,Top=15,Width=420,Text="Имена VPN-адаптеров через ; (режим TUN/TAP):"}); f.Controls.Add(a);
            f.Controls.Add(new Label {Left=20,Top=88,Width=250,Text="Закрывать после потери VPN, секунд:"}); f.Controls.Add(delay);
            var auto=new CheckBox {Left=20,Top=125,Width=420,Text="Автоопределение TUN/TAP (MantaRay и другие VPN)",Checked=settings.AutoDetectTunnel}; f.Controls.Add(auto);
            f.Controls.Add(new Label {Left=20,Top=154,Width=420,Height=35,Text="Учитывается активный маршрут, а не просто открытое окно VPN."});
            var save=new Button {Left=320,Top=195,Width=120,Text="Сохранить",DialogResult=DialogResult.OK}; f.Controls.Add(save); f.AcceptButton=save;
            if(f.ShowDialog()==DialogResult.OK && (a.Text.Trim().Length>0 || auto.Checked)) { settings.Adapter=a.Text.Trim(); settings.AutoDetectTunnel=auto.Checked; settings.GraceSeconds=(int)delay.Value; Program.Save(settings); last=null; job=null; nextProbe=0; Program.Log("Настройки изменены"); }
        }
    }
    protected override void ExitThreadCore() { timer.Stop(); timer.Dispose(); tray.Visible=false; tray.Dispose(); launchEvent.Dispose(); Program.Log("Защита выключена пользователем"); base.ExitThreadCore(); }
}
