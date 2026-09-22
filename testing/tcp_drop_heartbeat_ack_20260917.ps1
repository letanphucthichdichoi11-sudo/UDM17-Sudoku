$ErrorActionPreference='Stop'
Add-Type -TypeDefinition @'
using System;
using System.IO;
using System.Net;
using System.Net.Sockets;
using System.Text;
using System.Text.RegularExpressions;
using System.Threading;
using System.Threading.Tasks;
public sealed class DropAckProxy : IDisposable {
    readonly TcpListener listener;
    TcpClient downstream, upstream;
    Task worker;
    int dropped;
    public int Port { get { return ((IPEndPoint)listener.LocalEndpoint).Port; } }
    public int Dropped { get { return dropped; } }
    public DropAckProxy() { listener=new TcpListener(IPAddress.Loopback,0); listener.Start(); worker=Task.Run((Action)Run); }
    static byte[] ReadExact(Stream s,int n) { byte[] b=new byte[n];int off=0;while(off<n){int got=s.Read(b,off,n-off);if(got==0) throw new EndOfStreamException();off+=got;}return b; }
    static byte[] ReadFrame(Stream s) {byte[] h=ReadExact(s,4);int n=(h[0]<<24)|(h[1]<<16)|(h[2]<<8)|h[3];if(n<1||n>1048576)throw new InvalidDataException("Frame length");byte[] p=ReadExact(s,n);byte[] f=new byte[n+4];Buffer.BlockCopy(h,0,f,0,4);Buffer.BlockCopy(p,0,f,4,n);return f;}
    static int Type(byte[] frame) {string json=Encoding.UTF8.GetString(frame,4,frame.Length-4);Match m=Regex.Match(json,"\\\"Type\\\"\\s*:\\s*(\\d+)");return m.Success?int.Parse(m.Groups[1].Value):-1;}
    void Run() {try{downstream=listener.AcceptTcpClient();upstream=new TcpClient("127.0.0.1",5000);Task a=Task.Run(()=>Pump(downstream.GetStream(),upstream.GetStream(),false));Task b=Task.Run(()=>Pump(upstream.GetStream(),downstream.GetStream(),true));Task.WaitAny(a,b);}catch{}finally{try{if(downstream!=null)downstream.Close();}catch{}try{if(upstream!=null)upstream.Close();}catch{}}}
    void Pump(Stream src,Stream dst,bool serverToClient) {try{while(true){byte[] f=ReadFrame(src);if(serverToClient&&Type(f)==3&&Interlocked.CompareExchange(ref dropped,1,0)==0)continue;dst.Write(f,0,f.Length);dst.Flush();}}catch{}}
    public void Dispose(){try{listener.Stop();}catch{}try{if(downstream!=null)downstream.Close();}catch{}try{if(upstream!=null)upstream.Close();}catch{}try{if(worker!=null)worker.Wait(1000);}catch{}}
}
'@

function Read-Exact($stream,[int]$count){$b=[byte[]]::new($count);$o=0;while($o -lt $count){$n=$stream.Read($b,$o,$count-$o);if($n -eq 0){throw 'EOF'};$o+=$n};return $b}
function Read-Frame($stream){$h=Read-Exact $stream 4;$n=([int]$h[0] -shl 24)-bor([int]$h[1] -shl 16)-bor([int]$h[2] -shl 8)-bor[int]$h[3];$json=[Text.Encoding]::UTF8.GetString((Read-Exact $stream $n));$m=$json|ConvertFrom-Json;return $m}
function Send-Frame($stream,[int]$type,$payload){$id=[guid]::NewGuid();$inner=$payload|ConvertTo-Json -Compress -Depth 10;$json=[ordered]@{ProtocolVersion=1;MessageId=$id;Type=$type;Payload=$inner}|ConvertTo-Json -Compress -Depth 10;$b=[Text.Encoding]::UTF8.GetBytes($json);$h=[byte[]]@([byte](($b.Length -shr 24)-band 255),[byte](($b.Length -shr 16)-band 255),[byte](($b.Length -shr 8)-band 255),[byte]($b.Length-band 255));$stream.Write($h,0,4);$stream.Write($b,0,$b.Length);$stream.Flush();return $id}
function Await-Response($stream,[guid]$id){$events=@();while($true){$m=Read-Frame $stream;if($m.CorrelationId -eq $id){return [pscustomobject]@{Response=$m;Events=$events}};$events+=,$m}}

$tag=[guid]::NewGuid().ToString('N').Substring(0,8)
"RUN TC-099 tag=$tag at=$(Get-Date -Format o)"
$proxy=[DropAckProxy]::new();$client=[Net.Sockets.TcpClient]::new();$duplicate=$null
try {
    $client.ReceiveTimeout=1500;$client.SendTimeout=5000;$client.Connect('127.0.0.1',$proxy.Port);$stream=$client.GetStream()
    $player="ack-$tag"
    $helloId=Send-Frame $stream 0 @{PlayerId=$player;PlayerName='Ack Test'}
    $hello=Await-Response $stream $helloId
    if($hello.Response.Type -ne 1){throw 'Handshake failed'}
    $beat1=Send-Frame $stream 2 @{}
    $firstTimedOut=$false
    try{$null=Await-Response $stream $beat1}catch [System.Management.Automation.MethodInvocationException]{$firstTimedOut=$true}catch [System.IO.IOException]{$firstTimedOut=$true}
    $client.ReceiveTimeout=5000
    $beat2=Send-Frame $stream 2 @{}
    $second=Await-Response $stream $beat2
    $listId=Send-Frame $stream 8 @{}
    $list=Await-Response $stream $listId
    $duplicate=[Net.Sockets.TcpClient]::new('127.0.0.1',5000);$duplicate.ReceiveTimeout=5000
    $dupId=Send-Frame $duplicate.GetStream() 0 @{PlayerId=$player;PlayerName='Duplicate'}
    $dup=Await-Response $duplicate.GetStream() $dupId
    [pscustomobject]@{TestCase='TC-099';DroppedAckFrames=$proxy.Dropped;FirstHeartbeatTimedOut=$firstTimedOut;SecondHeartbeatAckType=$second.Response.Type;SecondCorrelationCorrect=($second.Response.CorrelationId -eq $beat2);SameConnectionListRoomsType=$list.Response.Type;DuplicateHandshakeType=$dup.Response.Type;NoDuplicateSession=($dup.Response.Type -eq 4);Timestamp=(Get-Date -Format o)}|ConvertTo-Json -Compress
}
finally {try{$duplicate.Close()}catch{};$client.Close();$proxy.Dispose()}
"END TC-099 at=$(Get-Date -Format o)"
