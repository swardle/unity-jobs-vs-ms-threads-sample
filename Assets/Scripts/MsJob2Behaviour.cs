//    Copyright 2022 Google LLC
//
//    Licensed under the Apache License, Version 2.0 (the "License");
//    you may not use this file except in compliance with the License.
//    You may obtain a copy of the License at
//
//        https://www.apache.org/licenses/LICENSE-2.0
//
//    Unless required by applicable law or agreed to in writing, software
//    distributed under the License is distributed on an "AS IS" BASIS,
//    WITHOUT WARRANTIES OR CONDITIONS OF ANY KIND, either express or implied.
//    See the License for the specific language governing permissions and
//    limitations under the License.
using System;
using System.Collections;
using System.Collections.Generic;
using System.Diagnostics;
using System.Threading.Tasks;
using System.Runtime.InteropServices;
using UnityEngine;
using UnityEngine.UI;
using UnityEngine.EventSystems;
using Unity.Burst;
using Unity.Jobs;
using Unity.Collections;


public class MsJob2Behaviour : MonoBehaviour
{
    public RawImage[] m_rawImages;
    public Toggle m_enableSystem;
    public Text m_timerText;
    private DropPoolJob[] m_dropPoolJobs;
    private DropPoolStart[] m_dropPoolStarts;
    private DropPoolStop[] m_dropPoolStops;
    private Texture2D[] m_textures;
    private int updateTick = 0;
    private Stopwatch m_timer = new Stopwatch();
    private readonly List<GCHandle> gcHandleStarts = new List<GCHandle>();
    private readonly List<GCHandle> gcHandleStops = new List<GCHandle>();
    private NativeArray<JobHandle>? jobHandles = null;
    private NativeArray<JobHandle>? jobHandlesStarts = null;
    private NativeArray<JobHandle>? jobHandlesStops = null;
    TimeSpan updateTime = new TimeSpan(0);


    // Common interface for executing something
    interface MyTask
    {
        void Execute();
    }

    private struct Job : IJob
    {
        public GCHandle handle;

        public void Execute()
        {
            MyTask task = (MyTask)handle.Target;
            task.Execute();
        }
    }

    class DropPoolStart : MyTask
    {
        public Stopwatch m_timer;

        public void Execute()
        {
            m_timer.Restart();
        }
    }

    class DropPoolStop : MyTask
    {
        public Stopwatch m_timer;

        public void Execute()
        {
            m_timer.Stop();
        }
    }

    [BurstCompile]

    struct DropPoolJob : IJob
    {
        public const int width = 512;
        public const int height = 512;
        public NativeArray<float> buffer1;
        public NativeArray<float> buffer2;
        public NativeArray<Color32> output;
        public NativeArray<int> frame;
        public NativeArray<Unity.Mathematics.Random> random;


        public void Clear()
        {
            random = new NativeArray<Unity.Mathematics.Random>(1, Allocator.Persistent);
            random[0] = new Unity.Mathematics.Random((uint)UnityEngine.Random.Range(1, 100000));

            buffer1 = new NativeArray<float>(width * height, Allocator.Persistent);
            buffer2 = new NativeArray<float>(width * height, Allocator.Persistent);
            output = new NativeArray<Color32>(width * height, Allocator.Persistent);
            frame = new NativeArray<int>(1, Allocator.Persistent);
            frame[0] = 0;

            Color32 gray = new Color32(0, 0, 0, 255);
            for (int y = 0; y < height; y++)
            {
                int yi = y * width;
                for (int x = 0; x < width; x++)
                {
                    int i = x + yi;
                    buffer1[i] = gray.r;
                    buffer2[i] = gray.r;
                    output[i] = gray;
                }
            }
        }

        public void Rain()
        {
            var lrandom = random[0];
            int x = lrandom.NextInt(1, width - 1);
            int y = lrandom.NextInt(1, height - 1);
            random[0] = lrandom;
            NativeArray<float> current = buffer1;
            NativeArray<float> previous = buffer2;
            // swap buffers every frame
            if ((frame[0] & 1) == 1)
            {
                current = buffer2;
                previous = buffer1;
            }

            int yi = y * DropPoolJob.width;
            int i = x + yi;
            previous[i] = 4096;
        }

        public void Execute()
        {
            float dampening = 0.99f;
            Rain();
            NativeArray<float> current = buffer1;
            NativeArray<float> previous = buffer2;
            // swap buffers every frame
            if ((frame[0] & 1) == 1)
            {
                current = buffer2;
                previous = buffer1;
            }
            frame[0]++;
            float val;
            byte bval;

            for (int y = 1; y < height - 1; y++)
            {
                int yi = y * width;
                for (int x = 1; x < width - 1; x++)
                {
                    int i = x + yi;
                    val = dampening * (
                           (
                             previous[i - 1] +
                                          previous[i + 1] +
                                          previous[i - width] +
                                          previous[i + width] +

                                          previous[i - width - 1] +
                                          previous[i - width + 1] +
                                          previous[i + width - 1] +
                                          previous[i + width + 1]
                                        ) / 4 -
                                        current[i]);
                    bval = (byte)Mathf.Clamp(val, 0, 255);
                    current[i] = val;

                    output[i] = new Color32(bval, bval, bval, 255);
                }
            }
        }
    }

    public void Awake()
    {
        m_textures = new Texture2D[m_rawImages.Length];
        m_dropPoolJobs = new DropPoolJob[m_rawImages.Length];
        m_dropPoolStarts = new DropPoolStart[m_rawImages.Length];
        m_dropPoolStops = new DropPoolStop[m_rawImages.Length];
        Stopwatch mytimer = new Stopwatch();
        for (int i = 0; i < m_rawImages.Length; i++)
        {
            ref DropPoolJob dropPoolJob = ref m_dropPoolJobs[i];
            dropPoolJob.Clear();
            RawImage rawImage = m_rawImages[i];
            m_dropPoolStarts[i] = new DropPoolStart()
            {
                m_timer = mytimer
            };
            m_dropPoolStops[i] = new DropPoolStop()
            {
                m_timer = mytimer
            };
            // Add pools to the GC as they will be used in a job.
            GCHandle gcHandleStart = GCHandle.Alloc(m_dropPoolStarts[i]);
            gcHandleStarts.Add(gcHandleStart);
            GCHandle gcHandleStop = GCHandle.Alloc(m_dropPoolStops[i]);
            gcHandleStops.Add(gcHandleStop);

            m_textures[i] = new Texture2D(DropPoolJob.width, DropPoolJob.height, TextureFormat.RGBA32, false);
            Texture2D texture = m_textures[i];

            texture.filterMode = FilterMode.Point;
            texture.SetPixelData(dropPoolJob.output, 0, 0);
            texture.Apply();
            rawImage.texture = texture;
        }
    }

    public void OnDestroy()
    {
        if (jobHandlesStops.HasValue)
        {
            JobHandle.CompleteAll(jobHandlesStops.Value);
            JobHandle.CompleteAll(jobHandlesStarts.Value);
            JobHandle.CompleteAll(jobHandles.Value);
            jobHandlesStarts.Value.Dispose();
            jobHandles.Value.Dispose();
            jobHandlesStops.Value.Dispose();
            jobHandlesStarts = null;
            jobHandles = null;
            jobHandlesStops = null;
        }
        for (int i = 0; i < gcHandleStarts.Count; i++)
        {
            // dec the reference to that we inc at GCHandle.Alloc
            gcHandleStarts[i].Free();
        }
        gcHandleStarts.Clear();
        for (int i = 0; i < gcHandleStops.Count; i++)
        {
            // dec the reference to that we inc at GCHandle.Alloc
            gcHandleStops[i].Free();
        }
        gcHandleStops.Clear();

        for (int i = 0; i < m_rawImages.Length; i++)
        {
            ref DropPoolJob dropPoolJob = ref m_dropPoolJobs[i];
            dropPoolJob.buffer1.Dispose();
            dropPoolJob.buffer2.Dispose();
            dropPoolJob.output.Dispose();
            dropPoolJob.frame.Dispose();
            dropPoolJob.random.Dispose();
        }

    }

    public void Update()
    {
        m_timer.Restart();
        if (m_enableSystem.isOn)
        {
            for (int i = 0; i < m_rawImages.Length; i++)
            {
                RawImage rawImage = m_rawImages[i];
                Texture2D texture = m_textures[i];
                ref DropPoolJob dropPoolJob = ref m_dropPoolJobs[i];
                texture.SetPixelData(dropPoolJob.output, 0, 0);
                texture.Apply();
                rawImage.texture = texture;
            }

            jobHandlesStarts = new NativeArray<JobHandle>(m_rawImages.Length, Allocator.Persistent);
            jobHandles = new NativeArray<JobHandle>(m_rawImages.Length, Allocator.Persistent);
            jobHandlesStops = new NativeArray<JobHandle>(m_rawImages.Length, Allocator.Persistent);
            NativeArray<JobHandle> jobHandeStartsRef = jobHandlesStarts.Value;
            NativeArray<JobHandle> jobHandesRef = jobHandlesStarts.Value;
            NativeArray<JobHandle> jobHandeStopsRef = jobHandlesStarts.Value;
            for (int i = 0; i < m_rawImages.Length; i++)
            {
                Job startJob = new Job()
                {
                    handle = gcHandleStarts[i]
                };

                Job stopJob = new Job()
                {
                    handle = gcHandleStops[i]
                };

                ref DropPoolJob dropPool = ref m_dropPoolJobs[i];

                // We remember the JobHandle so we can complete it later
                jobHandeStartsRef[i] = startJob.Schedule();
                // We remember the JobHandle so we can complete it later
                jobHandesRef[i] = dropPool.Schedule(jobHandeStartsRef[i]);
                // We remember the JobHandle so we can complete it later
                jobHandeStopsRef[i] = stopJob.Schedule(jobHandesRef[i]);
            }
            JobHandle.ScheduleBatchedJobs();
        }
        m_timerText.text = $"Ms Burst = {updateTime.TotalMilliseconds:0.##}";
    }

    public void LateUpdate()
    {
        if (jobHandlesStops.HasValue)
        {
            JobHandle.CompleteAll(jobHandlesStops.Value);
            JobHandle.CompleteAll(jobHandlesStarts.Value);
            JobHandle.CompleteAll(jobHandles.Value);
            jobHandlesStarts.Value.Dispose();
            jobHandles.Value.Dispose();
            jobHandlesStops.Value.Dispose();
            jobHandlesStarts = null;
            jobHandles = null;
            jobHandlesStops = null;
        }

        m_timer.Stop();
        updateTime = TimeSpan.FromTicks(m_timer.ElapsedTicks);
        updateTick++;
    }


    private Vector2 TryToClickPool(PointerEventData eventData, RawImage rawImage, int image)
    {
        Vector2 result = new Vector2(-1.0f, -1.0f);
        Vector2 clickPosition = eventData.position;
        RectTransform thisRect = rawImage.GetComponent<RectTransform>();

        bool inImage = RectTransformUtility.RectangleContainsScreenPoint(thisRect, clickPosition, null);
        if (inImage)
        {
            RectTransformUtility.ScreenPointToLocalPointInRectangle(thisRect, clickPosition, null, out result);
            result += thisRect.sizeDelta / 2;
            result /= thisRect.sizeDelta;
            result *= new Vector2(DropPoolJob.width, DropPoolJob.height);
            ref DropPoolJob dropPoolJob = ref m_dropPoolJobs[image];
            NativeArray<float> current = dropPoolJob.buffer1;
            NativeArray<float> previous = dropPoolJob.buffer2;
            // swap buffers every frame
            if ((dropPoolJob.frame[0] & 1) == 1)
            {
                current = dropPoolJob.buffer2;
                previous = dropPoolJob.buffer1;
            }
            int x = (int)result.x;
            int y = (int)result.y;
            if (x == DropPoolJob.width || x == 0 || y == 0 || y == DropPoolJob.height)
            {
                return result;
            }
            int yi = y * DropPoolJob.width;
            int i = x + yi;
            previous[i] = 4096;

            return result;
        }
        return result;
    }

    public void MyOnClick(BaseEventData e)
    {
        PointerEventData eventData = (PointerEventData)e;
        for (int i = 0; i < m_rawImages.Length; i++)
        {
            RawImage rawImage = m_rawImages[i];
            TryToClickPool(eventData, rawImage, i);
        }
    }
}
