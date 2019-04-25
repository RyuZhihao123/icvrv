using System.Collections;
using System.Collections.Generic;
using System.Threading;
using UnityEngine;
using UnityEngine.UI;
using TreeManager;
using Pvr_UnitySDKAPI;

public class TreeController : MonoBehaviour
{
    //**********************场景相关的properties************************************/
    // Pico设备
    public GameObject m_picoSystem;  // Pico System
    public GameObject m_headCamera;  // 头盔 HeadCamera
    public GameObject m_controller0; // 手柄 Controller
    private Ray m_ctrlRay;           // 手柄的方向

    // UI控件
    public GameObject m_canvas;      // canvas: UI控件的幕布
    public Button m_btnIterateOnce;  // 按钮: Iterate for Once 迭代一次按钮
    public Button m_btnBrush;        // 按钮：根据Brush创建新的植物
    public Button m_btnClearAll;     // 按钮: Clear all data 清除按钮
    public Toggle m_toggleLasso;     // 开关按钮：是否绘制Lasso的check box
    public Toggle m_toggleFreeSketch;// 开关按钮：是否进行FreeSketck的check box
    public Toggle m_toggleBrush;     // 开关按钮：是否进行Brush的check box

    // 场景中的物体们
    public ParticleSystem m_particleSystem;  // 粒子系统（生成植物的绿光效果）

    public GameObject m_laserRender;      // 手柄发出的射线Laser
    public GameObject m_laserBall;        // 手柄射线末端的小红球（后期会删掉）
    public GameObject m_lassoRender;      // 套索Lasso的GameObject
    private List<Vector3> m_lassoPoints;  // 套索Lasso上的所有的点
    public GameObject m_SketchRender;     // 手绘枝干（FreeSketch）的GameObject
    private List<Vector3> m_sketchPoints;  // Free Sketch绘制的坐标点

    public List<GameObject> m_brushRender;       // 绘制Brush的GameObject（因为可能有多次Brush同时出现）
    public List<ParticleSystem> m_brushParticles; // 绘制Brush的ParticleSystem
    private List<List<Vector3>> m_brushPoints;   // Brush上的路径点（单纯记录的路径点）

    private List<GameObject> m_partobjects;   // 一些Basic的几何体
    private int m_targetIndex = -1;

    public Material m_leafMtl;       // 材质：树叶的材质
    public Material m_barkMtl;       // 材质：树干的材质

    //**********************Tree Modeling相关的properties************************************/

    // tree & algorithm
    private Tree3D m_tree;          // 【重中之重】我们的tree modeling算法都在这里
    private List<GameObject> m_treePart;        // 表示树干部分的GameObject
    private List<GameObject> m_leafPart;        // 表示树叶部分的GameObject
    private List<GameObject> m_selectedPart;    // 表示被“选中”部分的树干的GameObject;

    // 定义了手柄绘制时的目标"平面"信息（n为平面法线，a0为平面上一点）
    private Vector3 m_a0;
    private Vector3 m_n;

    // 【开关】
    private bool m_isLassoMouseDone = false; // 手柄是否在绘制套索Lasso
    private bool m_isFreeSketched = false;   // 手柄是否在进行FreeSketch
    private bool m_isBrushMouseDone = false; // 手柄是否在进行Brush绘制

    private Thread m_thread;   // 【线程】，因为生成算法耗时，因此生成过程放在线程中完成

    // 无需关注的属性
    public Button m_btbTest;       // 【UI测试】（备注：目前UI点击事件在Pico中无效）
    public Text m_Text;
    public Transform m_ctrldirection;

    /// <summary>
    /// Start()函数是在程序开始是执行的初始化函数
    /// </summary>
    void Start()
    {
        m_tree = new Tree3D();
        m_tree.InitTree();    // 创建最初的芽

        m_thread = null;
        m_treePart = new List<GameObject>();
        m_leafPart = new List<GameObject>();
        m_selectedPart = new List<GameObject>();
        m_lassoPoints = new List<Vector3>();
        m_sketchPoints = new List<Vector3>();
        m_brushRender = new List<GameObject>();
        m_brushPoints = new List<List<Vector3>>();
        m_brushParticles = new List<ParticleSystem>();
        m_partobjects = new List<GameObject>();

        m_ctrlRay = new Ray();

        this.GetComponent<MeshFilter>().mesh.Clear();

        m_btbTest.onClick.AddListener(OnClick); 
    }

    /// <summary>
    /// Update()函数每一帧的更新函数（以很高的频率循环被调用）
    /// </summary>
    void Update()
    {
        RayCasting_Controller();   // 【重要】处理pico手柄的事件

        if(IsThreadFinished())    // 如果更新植物数据的线程结束了，则创建新的模型
        {
            Debug.Log("线程结束进行刷新");
            m_tree.ClearAllMarkerPoints();
            ClearBrushMarkerPoints();

            UpdateTreeObjects();  // 根据当前的m_tree生成新的Mesh模型

            m_thread = null;      // 结束线程

            // 清除粒子系统（绿色的光）
            m_particleSystem.Clear();     
            var em = m_particleSystem.emission;
            em.enabled = false;


            // 解出事件锁
            m_tree.m_isUpdateMesh = false;    
            m_tree.m_isUpdateMeshDone = true;
        }
    }

    /// <summary>
    /// 清除所有的数据
    /// </summary>
    public void ClearAllData()
    {
        foreach (GameObject m in m_treePart)   // 我也不知道C#会不会自动释放，但是保险起见还是手动release掉了
            Destroy(m);
        foreach (GameObject m in m_leafPart)
            Destroy(m);
        foreach (GameObject m in m_selectedPart)
            Destroy(m);

        m_treePart.Clear();
        m_leafPart.Clear();
        m_selectedPart.Clear();  
        
        m_tree.ClearAllData();
    }

    public void ClearBrushMarkerPoints()
    {
        foreach (ParticleSystem ps in m_brushParticles)
            Destroy(ps);
        m_brushParticles.Clear();
        foreach (GameObject m in m_brushRender)
            Destroy(m);
        m_brushRender.Clear();
        m_brushPoints.Clear();
    }

    /// <summary>
    /// 点击“Iterate for Once”按钮的事件
    /// </summary>
    public void OnClick_IterateOnce()
    {
        if (m_thread != null && m_thread.ThreadState == ThreadState.Running)
            return;
        if (m_tree.m_isUpdateMesh)
            return;
        if (!m_tree.m_isUpdateMeshDone)
            return;
        if (m_tree.m_growthMode != Tree3D.GrowthMode._Free)
            return;

        // Create MarkerPoints
        m_tree.UpdateBranchLength(); 
        m_tree.RecreateMarkerPoints();

        // 开启事件锁
        m_tree.m_isUpdateMesh = false;
        m_tree.m_isUpdateMeshDone = false;

        // 开启粒子系统
        var em = m_particleSystem.emission;
        em.enabled = true;

        List<Vector3> location = m_tree.GetBudLocationList();

        // 更新粒子系统的数据
        ParticleSystem.Particle[] particles_arr = new ParticleSystem.Particle[location.Count];
        m_particleSystem.Emit(location.Count);
        m_particleSystem.GetParticles(particles_arr);

        for (int i = 0; i < location.Count; i++)
        {
            particles_arr[i].position = location[i];
        }
        // build the particle system
        m_particleSystem.SetParticles(particles_arr, particles_arr.Length);
        em.enabled = false;

        // 开启线程，进行植物的生成
        m_thread = new Thread(new ThreadStart(SubThreadIterOnce));
        m_thread.Start();
    }

    /// <summary>
    /// 在Lasso模式下，释放鼠标会根据当前Lasso生成新的植物
    /// </summary>
    public void OnMouseRelease_LassoMode()
    {
        if (m_thread != null && m_thread.ThreadState == ThreadState.Running)
            return;
        if (m_tree.m_isUpdateMesh)
            return;
        if (!m_tree.m_isUpdateMeshDone)
            return;
        if (m_tree.m_growthMode != Tree3D.GrowthMode._Lasso)
            return;

        // Create MarkerPoints
        Vector3 dir = new Vector3(m_controller0.transform.position.x, 0.0f, m_controller0.transform.position.z);
        m_tree.RecreateMarkerPointByLasso(m_lassoPoints,
            Vector3.Cross(dir, new Vector3(0.0f, 1.0f, 0.0f)).normalized,
            dir.normalized);

        // update particle system
        m_tree.m_isUpdateMesh = false;
        m_tree.m_isUpdateMeshDone = false;

        var em = m_particleSystem.emission;
        em.enabled = true;

        List<float[]> location = m_tree.m_KDmarkerPoints;

        ParticleSystem.Particle[] particles_arr = new ParticleSystem.Particle[100];
        m_particleSystem.Emit(particles_arr.Length);
        m_particleSystem.GetParticles(particles_arr);

        for (int i = 0; i < 100; i++)
            particles_arr[i].position = new Vector3(location[i][0], location[i][1], location[i][2]);

        // build the particle system
        m_particleSystem.SetParticles(particles_arr, particles_arr.Length);
        em.enabled = false;

        // 开启生成植物的线程
        m_thread = new Thread(new ThreadStart(SubThreadLasso));
        m_thread.Start();
    }

    public void Onclick_BrushGenerate()
    {
        if (m_thread != null)
            return;

        // 开启生成植物的线程
        if (m_tree.kdtree != null)
            m_tree.kdtree.Clear();

        m_tree.kdtree = m_tree.BuildKDTree();
        Debug.Log("MarkerPoints" + m_tree.m_KDmarkerPoints.Count.ToString());

        m_tree.SetupParameters(Tree3D.GrowthMode._Brush);   // 修改一波参数

        m_thread = new Thread(new ThreadStart(SubThreadBrush));
        m_thread.Start();
    }

    /*******子线程：点击按钮“Iterate for once”后触发***/
    private void SubThreadIterOnce()
    {
        m_tree.btn_iterateOnce();  // 生成植物的一轮迭代
    }

    /*******子线程：Lasso模式下，释放鼠标后触发*************************/
    private void SubThreadLasso()
    {
        while(true)  //不断地进行迭代，直到植物不再生长为止
        {
            m_tree.m_isModified = false; 
            m_tree.btn_iterateOnce();

            if (!m_tree.m_isModified)  // 如果本轮迭代tree的数据没有得到更新，结束循环
                break;
        }
    }

    private void SubThreadBrush()
    {
        while (true)  //不断地进行迭代，直到植物不再生长为止
        {
            m_tree.m_isModified = false;
            m_tree.btn_iterateOnce();

            if (!m_tree.m_isModified)  // 如果本轮迭代tree的数据没有得到更新，结束循环
                break;
        }
    }


    /********返回当前线程是否结束**************/
    private bool IsThreadFinished()
    {
        return m_thread!=null && this.m_tree.m_isUpdateMesh && m_thread.ThreadState == ThreadState.Stopped;
    }

    /// <summary>
    /// 根据当前的m_tree信息更新树木的GameObject
    /// </summary>
    private void UpdateTreeObjects()
    {
        // 清除原有的tree parts
        foreach (GameObject m in m_treePart)
            Destroy(m);
        m_treePart.Clear();

        // 设置新的tree parts
        List<Mesh> meshes = m_tree.GetMesh(); // Get meshes from 'Tree'
        for (int i = 0; i < meshes.Count; i++)
        {
            GameObject obj = GameObject.CreatePrimitive(PrimitiveType.Cube);
            obj.transform.position = new Vector3(0.0f, 0.0f, 0.0f);
            //obj.name = "Mesh_" + i.ToString();
            obj.GetComponent<MeshFilter>().mesh = meshes[i];
            obj.GetComponent<MeshRenderer>().material = new Material(GetComponent<MeshRenderer>().material);
            obj.tag = "Mesh";
            MeshCollider collider = obj.AddComponent(typeof(MeshCollider)) as MeshCollider;

            m_treePart.Add(obj);
        }

        // 清除原有的leaf parts;
        foreach (GameObject m in m_leafPart)
            Destroy(m);
        m_leafPart.Clear();

        // 设置新的tree parts
        List<Mesh> leafMeshes = m_tree.GetLeafMesh(); // Get meshes from 'Leaf'

        for (int i = 0; i < leafMeshes.Count; i++)
        {
            GameObject obj = GameObject.CreatePrimitive(PrimitiveType.Cube);
            //obj.name = "Mesh_" + i.ToString();
            obj.GetComponent<MeshFilter>().mesh = leafMeshes[i];
            obj.GetComponent<MeshRenderer>().material = m_leafMtl;

            m_leafPart.Add(obj);
        }
    }

    /// <summary>
    /// 处理Pico手柄的事件：移动，点击等
    /// </summary>
    private void RayCasting_Controller()
    {
        // 更新controller的方向信息m_ctrlRay
        m_ctrlRay.origin = m_controller0.transform.position;
        m_ctrlRay.direction = m_ctrldirection.position - m_controller0.transform.position;

        // 处理手柄的一些操作
        if (HandleMovingEvent())
            return;

        // 处理新增几何体的操作
        if (AddNewPart())
            return;
        ModifiedObject();

        // 进行raycast
        RaycastHit hitInfo;
        if (Physics.Raycast(m_ctrlRay, out hitInfo))  // 首先保证线程thread停止，如果指向某个collider
        {
            GameObject clickObj = hitInfo.collider.gameObject;

            // 更新手柄射线Laser信息
            m_laserRender.GetComponent<LineRenderer>().SetPosition(0, m_controller0.transform.position);
            m_laserRender.GetComponent<LineRenderer>().SetPosition(1, hitInfo.point);

            // 更新手柄射线末端的红球球
            m_laserBall.transform.position = hitInfo.point;

            if (this.m_thread == null)  // 如果当前没有正在处理的线程thread，就处理TouchPad的点集事件
            {
                
                // 如果发生了TouchPad的点击事件
                if (Controller.UPvr_GetKeyDown(0, Pvr_KeyCode.TOUCHPAD) || Input.GetMouseButtonDown(0))
                {
                    Debug.Log("aaaaaaaaaaaa:" + clickObj.name);
                    // 【1】绘制：在选择FreeSketch模式的基础上，选择一段枝干，即可开始绘制
                    if(m_toggleFreeSketch.isOn && clickObj.CompareTag("Mesh"))
                    {
                        m_n = (m_picoSystem.transform.position - hitInfo.point).normalized;
                        m_n.y = 0.0f;
                        m_a0 = hitInfo.point;

                        m_isFreeSketched = true;
                    }

                    // 【2】绘制：如果选中了一个几何体，那么调整他的形态
                    if(clickObj.CompareTag("Capsule") && m_targetIndex < 0)
                    {
                        m_targetIndex = int.Parse(clickObj.name);
                        Debug.Log("Click" + m_targetIndex.ToString());
                    }

                    // 【6】绘制：点击Brush按钮，根据当前MarkerPoints创建新的植物
                    if (m_btnBrush.name == clickObj.name)
                        Onclick_BrushGenerate();

                    // 【2】按钮：IterateOnce按钮，迭代一次
                    if (m_btnIterateOnce.name == clickObj.name)
                    {
                        if(m_tree.m_growthMode == Tree3D.GrowthMode._Free)  // 仅在Free模式下有效
                            OnClick_IterateOnce();
                    }

                    // 【3】按钮：CheckBox Lasso，开启Lasso绘制模式
                    if (m_toggleLasso.name == clickObj.name)   
                    {
                        // 删除套索Lasso的点
                        m_lassoPoints.Clear();
                        m_lassoRender.GetComponent<LineRenderer>().positionCount = 0;

                        if (m_toggleLasso.isOn)
                            ChangeMode(Tree3D.GrowthMode._Free);
                        else
                            ChangeMode(Tree3D.GrowthMode._Lasso);
                    }

                    // 【5】按钮：CheckBox Brush，开启Brush的绘制模式
                    if (m_toggleBrush.name == clickObj.name)
                    {
                        if (m_toggleBrush.isOn)
                            ChangeMode(Tree3D.GrowthMode._Free);
                        else
                            ChangeMode(Tree3D.GrowthMode._Brush);
                    }

                    // 【4】按钮：CheckBox FreeSketch，开启FreeSketch绘制模式
                    if (m_toggleFreeSketch.name == clickObj.name)
                        m_toggleFreeSketch.isOn = !m_toggleFreeSketch.isOn;

                    // 【6】按钮：ClearAll按钮，清除所有数据
                    if (m_btnClearAll.name == clickObj.name)
                    {
                        m_tree.ClearAllMarkerPoints();
                        ClearBrushMarkerPoints();
                        ClearAllData();
                    }
                }
            }
            return;
        }

        // 绘制：在选择FreeSketch模式的基础上，且树为空，则绘制一段branch
        if (m_toggleFreeSketch.isOn && m_tree.IsEmpty()
            && (Controller.UPvr_GetKeyDown(0, Pvr_KeyCode.TOUCHPAD) || Input.GetMouseButtonDown(0)))
        {
            m_n = (m_picoSystem.transform.position - hitInfo.point).normalized;
            m_n.y = 0.0f;
            m_a0 = new Vector3(0.0f,0.0f,0.0f);

            m_isFreeSketched = true;
        }

        if (!m_toggleFreeSketch.isOn)
        {
            DrawingBrush(); // 必须放置在这里，因为该函数修改到了MarkerPoints，如果点击了Button Brush之后，仍会触发造成Index越界
            DrawingLasso();
        }
        else
        {
            DrawingFreeSketch();
        }
    }

    private bool AddNewPart()
    {
        if (Controller.UPvr_GetKeyDown(0, Pvr_KeyCode.VOLUMEDOWN) || Input.GetKeyDown(KeyCode.C))
        {
            // 绘制平面
            m_n = (m_picoSystem.transform.position - new Vector3(0, 0, 0)).normalized;
            m_n.y = 0;
            m_a0 = new Vector3(0, 0, 0);

            Vector3 p0 = m_ctrlRay.origin;
            Vector3 u = m_ctrlRay.direction.normalized;
            float t = (Vector3.Dot(m_n, m_a0) - Vector3.Dot(m_n, p0)) / Vector3.Dot(m_n, u);
            Vector3 p = p0 + t * u;  // p为手柄射线与平面(n,a)的相交点

            GameObject obj = GameObject.CreatePrimitive(PrimitiveType.Capsule);
            obj.name = m_partobjects.Count.ToString();
            obj.tag = "Capsule";
            obj.transform.position = p;

            m_partobjects.Add(obj);
            return true;
        }
        return false;
    }

    private void ModifiedObject()
    {
        if (m_targetIndex < 0)
            return;

        // 绘制平面
        m_n = (m_picoSystem.transform.position - new Vector3(0, 0, 0)).normalized;
        m_n.y = 0;
        m_a0 = new Vector3(0, 0, 0);

        Vector3 p0 = m_ctrlRay.origin;
        Vector3 u = m_ctrlRay.direction.normalized;
        float t = (Vector3.Dot(m_n, m_a0) - Vector3.Dot(m_n, p0)) / Vector3.Dot(m_n, u);
        Vector3 p = p0 + t * u;  // p为手柄射线与平面(n,a)的相交点

        m_partobjects[m_targetIndex].transform.position = p;
        Debug.Log("xxxxxx" + m_targetIndex.ToString());
        m_partobjects[m_targetIndex].transform.localRotation = m_picoSystem.transform.localRotation;
        if(Controller.UPvr_GetKeyUp(0,Pvr_KeyCode.TOUCHPAD) || Input.GetMouseButtonUp(0))
        {
            m_targetIndex = -1;
        }
    }

    /***************一些手柄的交互式操作*****************/
    private void DrawingLasso()
    {
        // 处理鼠标点击事件（开始绘制Lasso)
        if (m_toggleLasso.isOn &&
            (Controller.UPvr_GetKeyDown(0, Pvr_KeyCode.TOUCHPAD) || Input.GetMouseButtonDown(0)))
        {
            m_lassoPoints.Clear();
            m_lassoRender.GetComponent<LineRenderer>().positionCount = 0;

            m_isLassoMouseDone = true;

            m_n = (m_picoSystem.transform.position - new Vector3(0, 0, 0)).normalized;
            m_n.y = 0;
            m_a0 = new Vector3(0, 0, 0);
        }
        // 处理鼠标释放事件（Lasso绘制结束）
        if (m_isLassoMouseDone &&
            (Controller.UPvr_GetKeyUp(0, Pvr_KeyCode.TOUCHPAD) || Input.GetMouseButtonUp(0)))
        {
            m_isLassoMouseDone = false;
            m_tree.ClearAllMarkerPoints();
            ClearAllData();

            if (m_lassoPoints.Count > 5)  // Lasso结点足够多，则根据Lasso创建植物
                OnMouseRelease_LassoMode();
        }
        // 处理鼠标滑动事件2（绘制Lasso中）
        if (m_isLassoMouseDone)
        {
            Vector3 p0 = m_ctrlRay.origin;
            Vector3 u = m_ctrlRay.direction.normalized;
            float t = (Vector3.Dot(m_n, m_a0) - Vector3.Dot(m_n, p0)) / Vector3.Dot(m_n, u);

            Vector3 p = p0 + t * u;  // p为手柄射线与平面(n,a)的相交点

            if (m_lassoPoints.Count == 0   // 如果手柄移动量足够大
                || Vector3.Distance(p, m_lassoPoints[m_lassoPoints.Count - 1]) > 0.3)
            {

                m_lassoRender.GetComponent<LineRenderer>().positionCount = m_lassoPoints.Count;
                m_lassoPoints.Add(p);
                m_lassoRender.GetComponent<LineRenderer>().SetPositions(
                    m_lassoPoints.ToArray());
            }
        }
    }
    private List<float[]> m_currentParticles = new List<float[]>();

    private void DrawingBrush()
    {
        if (!m_toggleBrush.isOn)
            return;
        if (m_thread != null && m_thread.ThreadState == ThreadState.Running)
            return;
        
        // 处理鼠标点击事件（开始绘制某段Brush）
        if(Controller.UPvr_GetKeyDown(0,Pvr_KeyCode.TOUCHPAD) || Input.GetMouseButtonDown(0))
        {
            m_isBrushMouseDone = true;
            m_currentParticles.Clear();

            // 创建新的点序列
            m_brushPoints.Add(new List<Vector3>());
            // 创建新的LineRenderer;
            GameObject obj = new GameObject();
            obj.name = "Brush" + m_brushPoints.Count;
            obj.AddComponent(typeof(LineRenderer));
            obj.GetComponent<LineRenderer>().startWidth = 0.1f;
            obj.GetComponent<LineRenderer>().endWidth = 0.1f;
            m_brushRender.Add(obj);
            // 创建新的BrushParticles
            ParticleSystem ps = Instantiate(m_particleSystem);
            ps.startColor = new Color(Random.Range(0.0f, 9.0f), Random.Range(0.0f, 9.0f), Random.Range(0.0f, 9.0f));
            m_brushParticles.Add(ps);

            m_n = (m_picoSystem.transform.position - new Vector3(0, 0, 0)).normalized;
            m_n.y = 0;
            m_a0 = new Vector3(0, 0, 0);
        }

        // 如果正在绘制Brush
        if(m_isBrushMouseDone)
        {
            Vector3 p0 = m_ctrlRay.origin;
            Vector3 u = m_ctrlRay.direction.normalized;
            float t = (Vector3.Dot(m_n, m_a0) - Vector3.Dot(m_n, p0)) / Vector3.Dot(m_n, u);

            Vector3 p = p0 + t * u;  // p为手柄射线与平面(n,a)的相交点

            int index = m_brushPoints.Count-1;

            if (m_brushPoints[index].Count == 0   // 如果手柄移动量足够大
                || Vector3.Distance(p, m_brushPoints[index][m_brushPoints[index].Count - 1]) > 0.5)
            {

                m_brushRender[index].GetComponent<LineRenderer>().positionCount = m_brushPoints[index].Count;
                m_brushPoints[index].Add(p);
                m_brushRender[index].GetComponent<LineRenderer>().SetPositions(
                    m_brushPoints[index].ToArray());

                if(m_brushPoints[index].Count>=2)
                {
                    // 基于当前Brush点生成新的MarkerPoints
                    List<float[]> location = m_tree.UpdateMarkerPointsByBrush(m_brushPoints[index][m_brushPoints[index].Count - 2],
                        m_brushPoints[index][m_brushPoints[index].Count-1]);

                    m_currentParticles.AddRange(location);
                    ParticleSystem ps = m_brushParticles[m_brushParticles.Count - 1];

                    var em = ps.emission;
                    em.enabled = true;

                    ParticleSystem.Particle[] particles_arr = new ParticleSystem.Particle[m_currentParticles.Count];
                    ps.Emit(particles_arr.Length);
                    ps.maxParticles = 10000;
                    ps.GetParticles(particles_arr);

                    for(int i=0; i< m_currentParticles.Count;i+=40)
                        particles_arr[i].position = new Vector3(m_currentParticles[i][0], m_currentParticles[i][1], m_currentParticles[i][2]);

                    // build the particle system
                    ps.SetParticles(particles_arr, particles_arr.Length);
                    em.enabled = false;

                }
            }
        }

        if(Controller.UPvr_GetKeyUp(0,Pvr_KeyCode.TOUCHPAD) || Input.GetMouseButtonUp(0))
        {
            m_isBrushMouseDone = false;
            m_tree.RecreateBuds();  // 重新生成Bud
        }
    }

    private void DrawingFreeSketch()
    {
        // 处理鼠标释放事件（FreeSketch绘制结束）
        if (m_isFreeSketched &&
            (Controller.UPvr_GetKeyUp(0, Pvr_KeyCode.TOUCHPAD) || Input.GetMouseButtonUp(0)))
        {
            m_isFreeSketched = false;

            m_tree.AddNewBranch(m_sketchPoints);
            UpdateTreeObjects();

            m_sketchPoints.Clear();
            m_SketchRender.GetComponent<LineRenderer>().positionCount = 0;
        }
        // 处理鼠标滑动事件（FreeSketch绘制中）
        if (m_isFreeSketched)
        {
            Vector3 p0 = m_ctrlRay.origin;
            Vector3 u = m_ctrlRay.direction.normalized;
            float t = (Vector3.Dot(m_n, m_a0) - Vector3.Dot(m_n, p0)) / Vector3.Dot(m_n, u);

            Vector3 p = p0 + t * u;  // p为手柄射线与平面(n,a)的相交点

            if (m_sketchPoints.Count == 0   // 如果手柄的位移量最够大
                || Vector3.Distance(p, m_sketchPoints[m_sketchPoints.Count - 1]) > 1.0)
            {
                // 将点加入到lineRenderer中
                m_SketchRender.GetComponent<LineRenderer>().positionCount = m_sketchPoints.Count;
                m_sketchPoints.Add(p);
                m_SketchRender.GetComponent<LineRenderer>().SetPositions(
                    m_sketchPoints.ToArray());
            }
        }
    }

    private bool m_isTriggerDown = false;
    private bool HandleMovingEvent()
    {
        // 首先检测Trigger的按键判定
        if (Controller.UPvr_GetKeyDown(0, Pvr_KeyCode.TRIGGER))
            m_isTriggerDown = true;
        if (Controller.UPvr_GetKeyUp(0, Pvr_KeyCode.TRIGGER))
            m_isTriggerDown = false;

        // 处理Touching Pad的Moving Event
        if (m_isTriggerDown && Controller.UPvr_GetKey(0,Pvr_KeyCode.TOUCHPAD))   // 【重要】如果在PC模式下请注释掉该行
        {
            Vector2 _touchPos = Controller.UPvr_GetTouchPadPosition(0) - new Vector2(127.5f, 127.5f);
            Vector3 touchDir = new Vector3(_touchPos.x, 0.0f, _touchPos.y).normalized;
            Vector3 headDir = m_headCamera.GetComponent<Camera>().transform.forward.normalized;
            headDir.y = 0.0f;
            Vector3 movedir = Quaternion.FromToRotation(new Vector3(1.0f, 0.0f, 0.0f), headDir)*touchDir; // 移动的方向

            m_picoSystem.transform.position += movedir * _touchPos.magnitude * 0.001f;  // 移动头盔
        }

        // 以下为PC上处理移动（WASD）
        if (Input.GetKey(KeyCode.G))
        {
            m_tree.m_gravityFactor = - 1.0f;
        }
        if (Input.GetKey(KeyCode.A))
        {
            m_picoSystem.transform.RotateAround(new Vector3(0.0f, 0.0f, 0.0f), new Vector3(0.0f, 1.0f, 0.0f), 0.5f);
            //m_canvas.transform.RotateAround(new Vector3(0.0f, 0.0f, 0.0f), new Vector3(0.0f, 1.0f, 0.0f), 0.5f);
        }
        if (Input.GetKey(KeyCode.D))
        {
            m_picoSystem.transform.RotateAround(new Vector3(0.0f, 0.0f, 0.0f), new Vector3(0.0f, 1.0f, 0.0f), -0.5f);
            //m_canvas.transform.RotateAround(new Vector3(0.0f, 0.0f, 0.0f), new Vector3(0.0f, 1.0f, 0.0f), -0.5f);
        }
        if (Input.GetKey(KeyCode.W))
        {
            Vector3 t = m_headCamera.GetComponent<Camera>().transform.forward.normalized;
            Vector3 dir = new Vector3(t.x, 0.0f, t.z);
            m_picoSystem.transform.Translate(0.5f * dir, Space.World);
            //m_canvas.transform.Translate(0.5f * dir, Space.World);
        }
        if (Input.GetKey(KeyCode.S))
        {
            Vector3 t = m_headCamera.GetComponent<Camera>().transform.forward.normalized;
            Vector3 dir = -new Vector3(t.x, 0.0f, t.z);
            m_picoSystem.transform.Translate(0.5f * dir, Space.World);
            //m_canvas.transform.Translate(0.5f * dir, Space.World);
        }

        // 更新手柄球球和laser方向
        m_laserBall.transform.position = m_ctrlRay.origin + m_ctrlRay.direction.normalized * 100;

        m_laserRender.GetComponent<LineRenderer>().SetPosition(0, m_controller0.transform.position);
        m_laserRender.GetComponent<LineRenderer>().SetPosition(1, m_ctrlRay.origin + m_ctrlRay.direction.normalized * 100);

        return m_isTriggerDown;
    }

    /************************ 切换绘制模式 **************************/
    public void ChangeMode(Tree3D.GrowthMode mode)
    {
        if(mode == Tree3D.GrowthMode._Free)
        {
            m_toggleBrush.isOn = false;
            m_toggleLasso.isOn = false;
        }
        if(mode == Tree3D.GrowthMode._Lasso)
        {
            m_toggleLasso.isOn = true;
            m_toggleBrush.isOn = false;
        }
        if(mode == Tree3D.GrowthMode._Brush)
        {
            m_toggleBrush.isOn = true;
            m_toggleLasso.isOn = false;
        }

        m_tree.SetupParameters(mode);
    }

    /***********【UI测试】，这个函数在Pico设备中没有响应***********/
    int t_count = 0;
    public void OnClick()
    {
        m_Text.text = "Click " + t_count.ToString();
        t_count++;
    }
}
