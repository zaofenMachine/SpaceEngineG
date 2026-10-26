using System;
using System.Collections;
using System.Collections.Concurrent;
using System.Collections.Generic;
using System.Diagnostics;
using System.IO;
using System.Text;
using System.Threading;
using UnityEngine;

namespace SpaceEngine.Runtime
{
    /// <summary>Verification-only, sampled game framebuffer recording. Never captures a desktop/window DC.</summary>
    public sealed class VerificationVideoRecorder:MonoBehaviour
    {
        [Serializable] public sealed class Stage
        { public string screenshot;public int videoFrameIndex,gameFrame;public double elapsedSeconds; }
        [Serializable] public sealed class Report
        {
            public bool passed;public string backend="Unity framebuffer ReadPixels + rawvideo pipe";
            public string scope="30fps wall-clock sampling at WaitForEndOfFrame, not every rendered game frame. Missing sample slots repeat the last actual framebuffer. RGB24 bottom-up input is flipped once by FFmpeg. Readback/encoding adds performance overhead.";
            public string startedUtc,firstFrameUtc,finishedUtc,productName,encoderPath,videoOutput,error;
            public double firstFrameEpoch,firstFrameRealtimeSeconds,timestampUncertaintySeconds=.1;
            public int playerPid,width,height,framesPerSecond=30,actualReadFrames,repeatedFrames,encodedFrames,encoderExitCode=-1,maximumQueueDepth;
            public double durationSeconds,wallSeconds,totalReadbackMilliseconds,maximumReadbackMilliseconds,totalQueueMilliseconds,maximumClockLeadSeconds;
            public List<Stage> stages=new List<Stage>();
        }
        sealed class Packet { public byte[] Pixels;public int RepeatPrevious; }
        public Report Result { get; private set; }
        public bool Running=>sampler!=null;
        const int Rate=30;
        readonly BlockingCollection<Packet> queue=new BlockingCollection<Packet>(8);
        readonly ConcurrentQueue<byte[]> buffers=new ConcurrentQueue<byte[]>();
        readonly Stopwatch clock=new Stopwatch();readonly StringBuilder encoderLog=new StringBuilder();
        Process encoder;Thread writer;Coroutine sampler;Texture2D texture;
        string directory;double origin=-1;int lastIndex=-1,lastReadGameFrame=-1,writtenFrames;
        volatile bool writerDone;volatile string writerError;
        int exitCode=-1;bool finished;

        public bool Begin(string encoderPath,string videoOutput,string evidenceDirectory,out string error)
        {
            error=null;directory=Path.GetFullPath(evidenceDirectory);
            Result=new Report{startedUtc=DateTime.UtcNow.ToString("O"),productName=Application.productName,
                playerPid=Process.GetCurrentProcess().Id,width=Screen.width,height=Screen.height,
                encoderPath=encoderPath,videoOutput=videoOutput};
            try
            {
                if(!Path.IsPathRooted(encoderPath)||!File.Exists(encoderPath))throw new FileNotFoundException("Video encoder must be an existing absolute path.",encoderPath);
                if(!Path.IsPathRooted(videoOutput))throw new ArgumentException("Video output must be an absolute path.");
                if(Screen.width!=1600||Screen.height!=900)throw new InvalidOperationException("Verification video requires a1600x900 game framebuffer.");
                Directory.CreateDirectory(directory);Directory.CreateDirectory(Path.GetDirectoryName(videoOutput));
                texture=new Texture2D(Screen.width,Screen.height,TextureFormat.RGB24,false,false);
                for(int i=0;i<10;i++)buffers.Enqueue(new byte[Screen.width*Screen.height*3]);
                var info=new ProcessStartInfo{FileName=encoderPath,UseShellExecute=false,CreateNoWindow=true,
                    RedirectStandardInput=true,RedirectStandardError=true,RedirectStandardOutput=false,
                    Arguments="-hide_banner -y -loglevel warning -nostats -f rawvideo -pixel_format rgb24 -video_size "+Screen.width+"x"+Screen.height+
                    " -framerate 30 -i pipe:0 -an -vf vflip -c:v libx264 -preset ultrafast -crf 20 -pix_fmt yuv420p -movflags +faststart "+Quote(videoOutput)};
                encoder=new Process{StartInfo=info};encoder.ErrorDataReceived+=(s,e)=>{if(e.Data!=null)lock(encoderLog)encoderLog.AppendLine(e.Data);};
                if(!encoder.Start())throw new InvalidOperationException("FFmpeg did not start.");
                encoder.BeginErrorReadLine();clock.Start();
                writer=new Thread(WriteFrames){IsBackground=true,Name="Verification framebuffer encoder pipe"};writer.Start();
                sampler=StartCoroutine(SampleFrames());return true;
            }
            catch(Exception e){Result.error=error=e.ToString();writerError=Result.error;
                if(writer==null&&encoder!=null)try{if(!encoder.HasExited)encoder.Kill();}catch(Exception){} return false;}
        }
        static string Quote(string value)
        {if(value.IndexOfAny(new[]{'"','\r','\n'})>=0)throw new ArgumentException("Invalid path characters.");return "\""+value+"\"";}
        IEnumerator SampleFrames()
        {
            var end=new WaitForEndOfFrame();
            while(writerError==null)
            {
                yield return end;
                if(origin<0||Math.Floor((clock.Elapsed.TotalSeconds-origin)*Rate)>lastIndex)ReadFramebuffer(false);
            }
        }
        bool ReadFramebuffer(bool force)
        {
            if(writerError!=null||texture==null)return false;
            if(Screen.width!=Result.width||Screen.height!=Result.height){writerError="Framebuffer dimensions changed during recording.";return false;}
            if(lastReadGameFrame==Time.frameCount)return true;
            double now=clock.Elapsed.TotalSeconds;
            if(origin<0)
            {
                origin=now;var stamp=DateTimeOffset.UtcNow;Result.firstFrameUtc=stamp.ToString("O");
                Result.firstFrameEpoch=stamp.ToUnixTimeMilliseconds()/1000.0;Result.firstFrameRealtimeSeconds=Time.realtimeSinceStartupAsDouble;
                Result.startedUtc=Result.firstFrameUtc;
            }
            int index=(int)Math.Floor((now-origin)*Rate);
            if(index<=lastIndex){if(!force)return true;index=lastIndex+1;}
            if(!buffers.TryDequeue(out byte[] pixels)){writerError="Framebuffer encoder buffer pool exhausted; no frames silently dropped.";return false;}
            var watch=Stopwatch.StartNew();RenderTexture previous=RenderTexture.active;
            try
            {
                RenderTexture.active=null;
                texture.ReadPixels(new Rect(0,0,Result.width,Result.height),0,0,false);
                // CPU copy only. Apply would upload this freshly read buffer back
                // to the GPU and is deliberately unnecessary here.
                texture.GetRawTextureData<byte>().CopyTo(pixels);
            }
            catch(Exception e){buffers.Enqueue(pixels);writerError=e.ToString();return false;}
            finally{RenderTexture.active=previous;}
            double milliseconds=watch.Elapsed.TotalMilliseconds;
            Result.totalReadbackMilliseconds+=milliseconds;Result.maximumReadbackMilliseconds=Math.Max(Result.maximumReadbackMilliseconds,milliseconds);
            watch.Restart();
            if(!queue.TryAdd(new Packet{Pixels=pixels,RepeatPrevious=lastIndex<0?0:index-lastIndex-1}))
            {buffers.Enqueue(pixels);writerError="Framebuffer queue reached8; recording failed instead of blocking flight or dropping frames.";return false;}
            Result.totalQueueMilliseconds+=watch.Elapsed.TotalMilliseconds;
            Result.maximumQueueDepth=Math.Max(Result.maximumQueueDepth,queue.Count);
            Result.actualReadFrames++;Result.repeatedFrames+=lastIndex<0?0:index-lastIndex-1;
            Result.maximumClockLeadSeconds=Math.Max(Result.maximumClockLeadSeconds,index/(double)Rate-(now-origin));
            lastIndex=index;lastReadGameFrame=Time.frameCount;return true;
        }
        /// <summary>Call at the same EOF as ScreenCapture.CaptureScreenshot for an exact video-frame comparison address.</summary>
        public void MarkScreenshot(string screenshot)
        {
            if(ReadFramebuffer(true))Result.stages.Add(new Stage{screenshot=screenshot,videoFrameIndex=lastIndex,
                gameFrame=Time.frameCount,elapsedSeconds=clock.Elapsed.TotalSeconds-origin});
        }
        void WriteFrames()
        {
            byte[] previous=null;
            try
            {
                Stream pipe=encoder.StandardInput.BaseStream;
                foreach(Packet packet in queue.GetConsumingEnumerable())
                {
                    for(int repeat=0;repeat<packet.RepeatPrevious;repeat++)
                    {if(previous==null)throw new InvalidOperationException("Cannot duplicate before the first actual frame.");pipe.Write(previous,0,previous.Length);Interlocked.Increment(ref writtenFrames);}
                    if(packet.Pixels==null)continue;
                    pipe.Write(packet.Pixels,0,packet.Pixels.Length);Interlocked.Increment(ref writtenFrames);
                    if(previous!=null)buffers.Enqueue(previous);previous=packet.Pixels;
                }
                pipe.Flush();pipe.Close();
                if(!encoder.WaitForExit(45000)){encoder.Kill();writerError="FFmpeg finalization exceeded45 seconds.";}
                else exitCode=encoder.ExitCode;
            }
            catch(Exception e){writerError=e.ToString();try{if(encoder!=null&&!encoder.HasExited)encoder.Kill();}catch(Exception){} }
            finally{if(previous!=null)buffers.Enqueue(previous);writerDone=true;}
        }
        public IEnumerator Finish()
        {
            if(finished)yield break;
            finished=true;if(sampler!=null){StopCoroutine(sampler);sampler=null;}
            if(origin>=0)
            {
                Result.wallSeconds=clock.Elapsed.TotalSeconds-origin;
                int total=Math.Max(lastIndex+1,(int)Math.Ceiling(Result.wallSeconds*Rate));
                int repeat=total-lastIndex-1;
                if(repeat>0&&writerError==null)
                {
                    if(queue.TryAdd(new Packet{RepeatPrevious=repeat}))Result.repeatedFrames+=repeat;
                    else writerError="Final framebuffer repeat packet could not be queued.";
                }
            }
            queue.CompleteAdding();
            double deadline=Time.realtimeSinceStartupAsDouble+50;
            while(writer!=null&&!writerDone&&Time.realtimeSinceStartupAsDouble<deadline)yield return null;
            if(writer!=null&&!writerDone){writerError="Framebuffer writer did not finish within50 seconds.";try{if(!encoder.HasExited)encoder.Kill();}catch(Exception){} }
            Result.encodedFrames=Interlocked.CompareExchange(ref writtenFrames,0,0);Result.durationSeconds=Result.encodedFrames/(double)Rate;
            Result.encoderExitCode=exitCode;Result.finishedUtc=DateTime.UtcNow.ToString("O");Result.error=writerError??Result.error;
            Result.passed=writerDone&&exitCode==0&&string.IsNullOrEmpty(Result.error)&&Result.actualReadFrames>0
                &&Result.encodedFrames==Result.actualReadFrames+Result.repeatedFrames;
            lock(encoderLog)File.WriteAllText(Path.Combine(directory,"ffmpeg-native-encoder.log"),encoderLog.ToString());
            File.WriteAllText(Path.Combine(directory,"native-video.json"),JsonUtility.ToJson(Result,true));
            if(texture!=null){Destroy(texture);texture=null;}
            if(writerDone&&encoder!=null){encoder.Dispose();encoder=null;}
        }
        void OnDestroy()
        {
            if(!queue.IsAddingCompleted)queue.CompleteAdding();
            if(!writerDone&&encoder!=null)try{if(!encoder.HasExited)encoder.Kill();}catch(Exception){}
            if(texture!=null)Destroy(texture);
        }
    }
}
