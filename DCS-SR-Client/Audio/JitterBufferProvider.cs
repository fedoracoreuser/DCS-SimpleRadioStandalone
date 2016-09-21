﻿using System;
using System.Collections.Generic;
using NAudio.Utils;
using NAudio.Wave;

namespace Ciribob.DCS.SimpleRadio.Standalone.Client.Audio
{
    public class JitterBufferProvider : IWaveProvider
    {
        private readonly CircularBuffer _circularBuffer;

        private readonly byte[] _silence = new byte[AudioManager.SEGMENT_FRAMES*2]; //*2 for stereo

        private readonly LinkedList<JitterBufferAudio> _bufferedAudio = new LinkedList<JitterBufferAudio>();

        private uint _lastRead; // gives current index

        private readonly object _lock = new object();
        private uint _missing; // counts missing packets

        public JitterBufferProvider(WaveFormat waveFormat)
        {
            WaveFormat = waveFormat;

            _circularBuffer = new CircularBuffer(WaveFormat.AverageBytesPerSecond*10);

            Array.Clear(_silence, 0, _silence.Length);
        }

        public WaveFormat WaveFormat { get; }

        public int Read(byte[] buffer, int offset, int count)
        {
            var read = 0;
            lock (_lock)
            {
                //need to return read equal to count

                //do while loop
                //break when read == count
                //each time round increment read
                //read becomes read + last Read

                do
                {
                    read = read + _circularBuffer.Read(buffer, offset + read, count - read);

                    if (read < count)
                    {
                        //now read in from the jitterbuffer
                        if (_bufferedAudio.Count == 0)
                        {
                            // zero the end of the buffer
                            Array.Clear(buffer, offset + read, count - read);
                            read = count;
                            //  Console.WriteLine("Buffer Empty");
                        }
                        else
                        {
                            var audio = _bufferedAudio.First.Value;
                            //no Pop?
                            _bufferedAudio.RemoveFirst();

                            if (_lastRead == 0)
                                _lastRead = audio.PacketNumber;
                            else
                            {
                                if (_lastRead + 1 < audio.PacketNumber)
                                {
                                    //fill with missing silence - will only add max of 5x Packet length but it could be a bunch of missing?
                                    var missing = audio.PacketNumber - (_lastRead + 1);

                                    //update counter for interest
                                    _missing += missing;

                                    //  Console.WriteLine("Missing Packet Total: "+_missing);

                                    var fill = Math.Min(missing, 5);

                                    for (var i = 0; i < fill; i++)
                                    {
                                        _circularBuffer.Write(_silence, 0, _silence.Length);
                                    }
                                }

                                _lastRead = audio.PacketNumber;
                            }

                            _circularBuffer.Write(audio.Audio, 0, audio.Audio.Length);
                        }
                    }
                } while (read < count);
            }
            return read;
        }

        public void AddSamples(JitterBufferAudio jitterBufferAudio)
        {
            lock (_lock)
            {
                //re-order if we can or discard

                //add to linked list
                //add front to back
                if (_bufferedAudio.Count == 0)
                {
                    _bufferedAudio.AddFirst(jitterBufferAudio);
                }
                else if (jitterBufferAudio.PacketNumber > _lastRead)
                {
                    for (var it = _bufferedAudio.First; it != null;)
                    {
                        //iterate list
                        //if packetNumber == curentItem
                        // discard
                        //else if packetNumber < _currentItem 
                        //add before
                        //else if packetNumber > _currentItem
                        //add before

                        //if not added - add to end?

                        var next = it.Next;

                        if (it.Value.PacketNumber == jitterBufferAudio.PacketNumber)
                        {
                            //discard! Duplicate packet
                            return;
                        }
                        if (jitterBufferAudio.PacketNumber < it.Value.PacketNumber)
                        {
                            _bufferedAudio.AddBefore(it, jitterBufferAudio);
                            return;
                        }
                        if ((jitterBufferAudio.PacketNumber > it.Value.PacketNumber) &&
                            ((next == null) || (jitterBufferAudio.PacketNumber < next.Value.PacketNumber)))
                        {
                            _bufferedAudio.AddAfter(it, jitterBufferAudio);
                            return;
                        }

                        it = next;
                    }
                }
            }
        }
    }
}