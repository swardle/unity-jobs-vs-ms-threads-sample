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
//    limitations under the License.using System;
using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Runtime.InteropServices;
using UnityEngine;
using UnityEngine.UI;
using UnityEngine.EventSystems;
using Unity.Jobs;
using Unity.Collections;


public class MsJobsBehaviour : MonoBehaviour
{
    public RawImage[] m_rawImages;
    public Toggle m_enableSystem;
    public Text m_timerText;
    private DropPool[] m_dropPools;
    private Texture2D[] m_textures;
    private int updateTick = 0;
    private Stopwatch m_timer = new Stopwatch();
    private readonly List<GCHandle> gcHandles = new List<GCHandle>();
    private NativeArray<JobHandle>? jobHandles = null;
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

    class DropPool : MyTask
    {
        public const int width = 512;
        public const int height = 512;
        public float[] buffer1 = new float[width * height];
        public float[] buffer2 = new float[width * height];
        public Color32[] output = new Color32[width * height];
        public int frame = 0;
        public Unity.Mathematics.Random random = new Unity.Mathematics.Random((uint)UnityEngine.Random.Range(1, 100000));
        public Stopwatch m_timer = new Stopwatch();

        public void Clear()
        {
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

        void Rain()
        {
            int x = random.NextInt(1, width - 1);
            int y = random.NextInt(1, height - 1);
            float[] current = buffer1;
            float[] previous = buffer2;
            // swap buffers every frame
            if ((frame & 1) == 1)
            {
                current = buffer2;
                previous = buffer1;
            }

            int yi = y * width;
            int i = x + yi;
            previous[i] = 4096;
        }


        public void Execute()
        {
            m_timer.Restart();
            Rain();
            float dampening = 0.99f;
            float[] current = buffer1;
            float[] previous = buffer2;
            // swap buffers every frame
            if ((frame & 1) == 1)
            {
                current = buffer2;
                previous = buffer1;
            }
            frame++;
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

                    output[x + (y * width)] = new Color32(bval, bval, bval, 255);
                }
            }
            m_timer.Stop();
        }
    }

    public void Awake()
    {
        m_dropPools = new DropPool[m_rawImages.Length];
        m_textures = new Texture2D[m_rawImages.Length];
        for (int i = 0; i < m_rawImages.Length; i++)
        {
            RawImage rawImage = m_rawImages[i];
            m_dropPools[i] = new DropPool();
            // Add pools to the GC as they will be used in a job.
            GCHandle gcHandle = GCHandle.Alloc(m_dropPools[i]);
            this.gcHandles.Add(gcHandle);

            DropPool dropPool = m_dropPools[i];
            m_textures[i] = new Texture2D(DropPool.width, DropPool.height, TextureFormat.RGBA32, false);
            Texture2D texture = m_textures[i];
            dropPool.Clear();

            texture.filterMode = FilterMode.Point;
            texture.SetPixels32(dropPool.output);
            texture.Apply();
            rawImage.texture = texture;
        }
    }

    public void OnDestroy()
    {
        if (jobHandles.HasValue)
        {
            JobHandle.CompleteAll(jobHandles.Value);
            jobHandles.Value.Dispose();
            jobHandles = null;
        }
        for (int i = 0; i < m_rawImages.Length; i++)
        {
            // dec the reference to that we inc at GCHandle.Alloc
            gcHandles[i].Free();
        }
        gcHandles.Clear();
    }

    public void Update()
    {
        m_timer.Restart();
        if (m_enableSystem.isOn)
        {
            for (int i = 0; i < m_rawImages.Length; i++)
            {
                RawImage rawImage = m_rawImages[i];
                DropPool dropPool = m_dropPools[i];
                Texture2D texture = m_textures[i];
                texture.SetPixels32(dropPool.output);
                texture.Apply();
                rawImage.texture = texture;
            }

            jobHandles = new NativeArray<JobHandle>(m_rawImages.Length, Allocator.Persistent);
            for (int i = 0; i < m_rawImages.Length; i++)
            {
                NativeArray<JobHandle> jobHandesRef = jobHandles.Value;
                Job job = new Job()
                {
                    handle = gcHandles[i]
                };

                // We remember the JobHandle so we can complete it later
                jobHandesRef[i] = job.Schedule();
            }
            JobHandle.ScheduleBatchedJobs();
        }

        m_timerText.text = $"Ms Job = {updateTime.TotalMilliseconds:0.##}";
    }

    public void LateUpdate()
    {
        if (jobHandles.HasValue)
        {
            JobHandle.CompleteAll(jobHandles.Value);
            jobHandles.Value.Dispose();
            jobHandles = null;
        }

        m_timer.Stop();
        updateTime = TimeSpan.FromTicks(m_timer.ElapsedTicks);
        updateTick++;
    }


    private Vector2 TryToClickPool(PointerEventData eventData, RawImage rawImage, DropPool dropPool)
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
            result *= new Vector2(DropPool.width, DropPool.height);
            float[] current = dropPool.buffer1;
            float[] previous = dropPool.buffer2;
            // swap buffers every frame
            if ((dropPool.frame & 1) == 1)
            {
                current = dropPool.buffer2;
                previous = dropPool.buffer1;
            }
            int x = (int)result.x;
            int y = (int)result.y;
            if (x == DropPool.width || x == 0 || y == 0 || y == DropPool.height)
            {
                return result;
            }
            int yi = y * DropPool.width;
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
            DropPool dropPool = m_dropPools[i];
            TryToClickPool(eventData, rawImage, dropPool);
        }
    }

}
