﻿/*
 * Copyright © 2017 EDDiscovery development team
 *
 * Licensed under the Apache License, Version 2.0 (the "License"); you may not use this
 * file except in compliance with the License. You may obtain a copy of the License at
 *
 * http://www.apache.org/licenses/LICENSE-2.0
 * 
 * Unless required by applicable law or agreed to in writing, software distributed under
 * the License is distributed on an "AS IS" BASIS, WITHOUT WARRANTIES OR CONDITIONS OF
 * ANY KIND, either express or implied. See the License for the specific language
 * governing permissions and limitations under the License.
 * 
 * EDDiscovery is not affiliated with Frontier Developments plc.
 */
using CSCore;
using CSCore.CoreAudioAPI;
using CSCore.DirectSound;
using CSCore.SoundOut;
using CSCore.Streams;
using CSCore.Streams.Effects;
using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;

namespace EDDiscovery.Audio
{
    class AudioDriverCSCore : IAudioDriver, IDisposable
    {
        public event AudioStopped AudioStoppedEvent;

        ISoundOut aout;

        public AudioDriverCSCore(string dev = null)
        {
            SetAudioEndpoint(dev,true);
        }

        public List<string> GetAudioEndpoints()
        {
            List<string> ep = new List<string>();
            IReadOnlyCollection<DirectSoundDevice> list = DirectSoundDeviceEnumerator.EnumerateDevices();
            foreach ( DirectSoundDevice d in list )
                ep.Add(d.Description);

            return ep;
        }

        public bool SetAudioEndpoint(string dev, bool usedefault = false)
        {
#if true
            System.Collections.ObjectModel.ReadOnlyCollection<DirectSoundDevice> list = DirectSoundDeviceEnumerator.EnumerateDevices();

            DirectSoundDevice dsd = null;
            if (dev != null)       // active selection
            {
                dsd = list.FirstOrDefault(x => x.Description.Equals(dev));        // find
                if (dsd == null && !usedefault) // if not found, and don't use the default (used by constructor)
                    return false;
            }

            DirectSoundOut dso = new DirectSoundOut(200, System.Threading.ThreadPriority.Highest);    // seems good quality at 200 ms latency

            if ( dsd != null )
                dso.Device = dsd.Guid;
#else
            MMDevice def = MMDeviceEnumerator.DefaultAudioEndpoint(DataFlow.Render, Role.Console);
            ISoundOut dso = new WasapiOut() { Latency = 100, Device = def  }; //BAD breakup
#endif

            if (aout != null)                 // clean up last
            {
                aout.Stopped -= Output_Stopped;
                aout.Stop();
                aout.Dispose();
            }

            aout = dso;
            aout.Stopped += Output_Stopped;

            return true;
        }

        public string GetAudioEndpoint()
        {
            Guid guid = ((DirectSoundOut)aout).Device;
            System.Collections.ObjectModel.ReadOnlyCollection<DirectSoundDevice> list = DirectSoundDeviceEnumerator.EnumerateDevices();
            DirectSoundDevice dsd = list.First(x => x.Guid == guid);
            return dsd.Description;
        }


        private void Output_Stopped(object sender, PlaybackStoppedEventArgs e)
        {
            //System.Diagnostics.Debug.WriteLine((Environment.TickCount % 10000).ToString("00000") + "Driver stopped");
            if (AudioStoppedEvent != null)
                AudioStoppedEvent();
        }

        public void Dispose()
        {
            aout.Dispose();
        }

        public void Dispose(Object o)
        {
            IWaveSource iws = o as IWaveSource;
            iws.Dispose();
            //System.Diagnostics.Debug.WriteLine("Audio disposed");
        }

        public void Start(Object o, int vol)
        {
            int t = Environment.TickCount;

            IWaveSource current = o as IWaveSource;
            aout.Initialize(current);
            //System.Diagnostics.Debug.WriteLine((Environment.TickCount-t).ToString("00000") + "Driver Init done");
            aout.Volume = (float)(vol) / 100;
            aout.Play();
            //System.Diagnostics.Debug.WriteLine((Environment.TickCount - t).ToString("00000") + "Driver Play done");
        }

        public void Stop()
        {
            aout.Stop();
        }

        // FROM file
        public Object Generate(string file, ConditionVariables effects)
        {
            try
            {
                IWaveSource s = CSCore.Codecs.CodecFactory.Instance.GetCodec(file);
                ApplyEffects(ref s, effects);
                return s;
            }
            catch
            {
                return null;
            }
        }

        // FROM audio stream
        public Object Generate(System.IO.Stream audioms, ConditionVariables effects, bool ensureaudio)
        {
            try
            {
                audioms.Position = 0;

                IWaveSource s;

                if (audioms.Length == 0)
                {
                    if (ensureaudio)
                        s = new NullWaveSource(50);
                    else
                        return null;
                }
                else
                {
                    s = new CSCore.Codecs.WAV.WaveFileReader(audioms);

                    //System.Diagnostics.Debug.WriteLine("oRIGINAL length " + s.Length);
                    //if (ensureaudio)
                      //  s = s.AppendSource(x => new ExtendWaveSource(x, 100));          // SEEMS to help the click at end..
                }

                //System.Diagnostics.Debug.WriteLine("Sample length " + s.Length);
                ApplyEffects(ref s, effects);
                //System.Diagnostics.Debug.WriteLine(".. to length " + s.Length);
                return s;
            }
            catch( Exception ex)
            {
                System.Diagnostics.Debug.WriteLine("Exception " + ex.Message);
                if (ensureaudio)
                    return new NullWaveSource(50);
                else
                    return null;
            }
        }

        public Object Add(Object last, string file, ConditionVariables effects)     // given a last sound, add more sound to it..
        {
            IWaveSource iws = (IWaveSource)Generate(file, effects);

            if (iws != null)
            {
                IWaveSource iwslast = (IWaveSource)last;

                if (iws.WaveFormat.Channels < iwslast.WaveFormat.Channels)      // need to adapt to previous format
                    iws = iws.ToStereo();
                else if (iws.WaveFormat.Channels > iwslast.WaveFormat.Channels)  
                    iws = iws.ToMono();

                if (iws.WaveFormat.SampleRate != iwslast.WaveFormat.SampleRate || iws.WaveFormat.BitsPerSample != iwslast.WaveFormat.BitsPerSample)
                    iws = ChangeSampleDepth(iws, iwslast.WaveFormat.SampleRate, iwslast.WaveFormat.BitsPerSample);

                iwslast = iwslast.AppendSource(x => new CopyWaveSource(iwslast, iws));

                return iwslast;
            }
            return null;
        }

        public Object Add(Object last, System.IO.Stream audioms, ConditionVariables effects , bool ensuresomeaudio)
        {
            IWaveSource iws = (IWaveSource)Generate(audioms, effects, ensuresomeaudio);

            if (iws != null)
            {
                IWaveSource iwslast = (IWaveSource)last;

                if (iws.WaveFormat.Channels < iwslast.WaveFormat.Channels)
                    iws = iws.ToStereo();
                else if (iws.WaveFormat.Channels > iwslast.WaveFormat.Channels)      // need to 
                    iws = iws.ToMono();

                if (iws.WaveFormat.SampleRate != iwslast.WaveFormat.SampleRate || iws.WaveFormat.BitsPerSample != iwslast.WaveFormat.BitsPerSample)
                    iws = ChangeSampleDepth(iws, iwslast.WaveFormat.SampleRate, iwslast.WaveFormat.BitsPerSample);

                iwslast = iwslast.AppendSource(x => new CopyWaveSource(iwslast, iws));

                return iwslast;
            }

            return null;
        }

        static private void ApplyEffects(ref IWaveSource src, ConditionVariables effect)
        {
            if (effect != null)
            {
                SoundEffectSettings ap = new SoundEffectSettings(effect);

                int extend = 0;
                if (ap.echoenabled)
                    extend = ap.echodelay * 2;
                if (ap.chorusenabled)
                    extend = Math.Max(extend, 50);
                if (ap.reverbenabled)
                    extend = Math.Max(extend, 50);

                if (extend > 0)
                {
                    //System.Diagnostics.Debug.WriteLine("Extend by " + extend + " ms due to effects");
                    src = src.AppendSource(x => new ExtendWaveSource(x, extend));
                }

                if (ap.chorusenabled)
                {
                    src = src.AppendSource(x => new DmoChorusEffect(x) { WetDryMix = ap.chorusmix, Feedback = ap.chorusfeedback, Delay = ap.chorusdelay, Depth = ap.chorusdepth });
                }

                if (ap.reverbenabled)
                {
                    src = src.AppendSource(x => new DmoWavesReverbEffect(x) { InGain = 0, ReverbMix = ap.reverbmix, ReverbTime = ((float)ap.reverbtime) / 1000.0F, HighFrequencyRTRatio = ((float)ap.reverbhfratio) / 1000.0F });
                }

                if (ap.distortionenabled)
                {
                    src = src.AppendSource(x => new DmoDistortionEffect(x) { Gain = ap.distortiongain, Edge = ap.distortionedge, PostEQCenterFrequency = ap.distortioncentrefreq, PostEQBandwidth = ap.distortionfreqwidth });
                }

                if (ap.gargleenabled)
                {
                    src = src.AppendSource(x => new DmoGargleEffect(x) { RateHz = ap.garglefreq });
                }

                if (ap.echoenabled)
                {
                    src = src.AppendSource(x => new DmoEchoEffect(x) { WetDryMix = ap.echomix, Feedback = ap.echofeedback, LeftDelay = ap.echodelay, RightDelay = ap.echodelay });
                }

                if ( ap.pitchshiftenabled )
                {
                    ISampleSource srs = src.ToSampleSource();
                    srs = srs.AppendSource(x => new PitchShifter(x) { PitchShiftFactor = ((float)ap.pitchshift)/100.0F });
                    src = srs.ToWaveSource();
                }
            }
        }

        public static IWaveSource ChangeSampleDepth(IWaveSource input, int destinationSampleRate , int bitdepth)
        {
            if (input == null)
                throw new ArgumentNullException("input");

            if (destinationSampleRate <= 0)
                throw new ArgumentOutOfRangeException("destinationSampleRate");

            if (input.WaveFormat.SampleRate == destinationSampleRate)
                return input;

            WaveFormat w = new WaveFormat(destinationSampleRate, bitdepth, input.WaveFormat.Channels);
            return new CSCore.DSP.DmoResampler(input, w);
        }
    }

    public class ExtendWaveSource : WaveAggregatorBase  // based on the TrimmedWaveSource in the CS Example
    {
        long extrabytes;
        long totalbytes;

        public ExtendWaveSource(IWaveSource ws, int ms) : base(ws)
        {
            extrabytes = ws.WaveFormat.MillisecondsToBytes(ms);
            totalbytes = base.Length + extrabytes;
            //System.Diagnostics.Debug.WriteLine("Extend by " + extrabytes + " at " + ws.WaveFormat.SampleRate);
        }

        public override long Length { get { return totalbytes; } }

        public override int Read(byte[] buffer, int offset, int count)
        {
            int read = BaseSource.Read(buffer, offset, count);      // want count, stored at offset
            //System.Diagnostics.Debug.WriteLine((Environment.TickCount % 10000).ToString() + " Extend " + offset + " Read " + count);

            if (read < count)
            {
                int left = count - read;      // what is left
                int totake = Math.Min(left, (int)extrabytes);

                //System.Diagnostics.Debug.WriteLine((Environment.TickCount % 10000).ToString() + " ++read " + read + " left " + left + " extend " + totake + " left " + (extrabytes - totake));

                if (totake > 0)
                {
                    Array.Clear(buffer, offset + read, totake);     // at offset+read, clear down to zero the extra bytes
                    extrabytes -= totake;
                }

                return read + totake;       // returned this total
            }

            return read;
        }
    }


    public class CopyWaveSource : WaveAggregatorBase  // based on the TrimmedWaveSource in the CS Example
    {
        IWaveSource other;

        public CopyWaveSource(IWaveSource ws, IWaveSource o) : base(ws)
        {
            other = o;
        }

        public override long Length { get { return base.Length + other.Length; } }

        public override int Read(byte[] buffer, int offset, int count)
        {
            int read = BaseSource.Read(buffer, offset, count);      // want count, stored at offset
            //System.Diagnostics.Debug.WriteLine((Environment.TickCount % 10000).ToString() + " Copy:Read " + count);

            if (read < count)
            {
                return other.Read(buffer, read, count - read);
            }
            else
                return read;
        }
    }


    public class NullWaveSource : IWaveSource  // empty audio
    {
        long pos = 0;
        long totalbytes = 0;
        private readonly WaveFormat _waveFormat;

        public NullWaveSource(int ms) 
        {
            _waveFormat = new WaveFormat(22050,16, 1, AudioEncoding.Pcm);
            totalbytes = _waveFormat.MillisecondsToBytes(ms);
        }

        public WaveFormat WaveFormat
        {
            get { return _waveFormat; }
        }


        public int Read(byte[] buffer, int offset, int count)
        {
            if ( pos < totalbytes)
            {
                int totake = Math.Min(count, (int)(totalbytes - pos));

                if ( totake > 0 )
                {
                    Array.Clear(buffer, offset , totake);     // at offset+read, clear down to zero the extra bytes
                }

                pos += totake;
                return totake;
            }

            return 0;
        }

        public long Length { get { return totalbytes; } set { } }
        public long Position { get { return pos; } set { } }
        public bool CanSeek
        {
            get { return false; }
        }

        public void Dispose()
        {
        }
    }


}
