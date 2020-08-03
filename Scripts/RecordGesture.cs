﻿using UnityEngine;
using UnityEngine.UI;
using System;
using System.Linq;
using System.Collections;
using System.Collections.Generic;
using System.IO; // needed for reading and writing .csv
using System.Text; // for csv 

[RequireComponent(typeof(Animator))]
public class RecordGesture: MonoBehaviour
{
    HumanPose currentPose = new HumanPose(); // keeps track of currentPose while animated
    HumanPose poseToSet; // reassemble poses from .csv data
    Animator animator;
    HumanPoseHandler poseHandler; // to record and retarget animation

    [Tooltip("Maximum number frames it can record.")]
    public int maxFrame = 10000; // max number of frames until unity stops recording automatically
    [Tooltip("Whether to loop the replayed animation.")]
    public bool isLoop = true; // whether to the loop the replayed animation
    [Tooltip("Save poses and target location for training purpose.")]
    public bool logPoses = false;
    [Tooltip("Activate self recording by mouse click")]
    public bool selfRecording = false;
    float[] currentMuscles; // an array containig current muscle values
    float[,] animationHumanPoses; // stack all currentHumanPose in one array
    int AnimationNumber = 0; // count animation file number
    int counterRec = 0; // count number of frames
    [HideInInspector]
    public int counterPlay = 0; // count animation playback frames
    int counterLoad = 0; // count number of frames of loaded animation

    // Note down position and rotation at start
    Vector3 positionAtStart;
    Vector3 currentPosition;
    Vector3 posePositionAtStart;
    Quaternion rotationAtStart;
    Quaternion currentRotation;
    Quaternion poseRotationAtStart;

    // Used to set poses
    int muscleCount; // count the number of muscles of the avatar
    bool recordPoses = false;
    bool reapplyPoses = false; // the recorded animation

    Academy_Agent academyAgent;

    // Uniy screen UI info
    Text recInfo;
    Text replayInfo;
    InputField replayFile;
    InputField recorderName;
    Button recStartEnd;
    Button recSave;
    Button replayStartEnd;
    Button replayLoad;
    Dropdown targetDropdown;
    Dropdown targetNumDropdown;
    Toggle KinectLeap;
    Toggle LeftRight;
    Toggle TrainTest;

    bool isTraining;

    void Start()
    {
        muscleCount = HumanTrait.MuscleCount; // count the number of muscles of the avatar
        animationHumanPoses = new float[maxFrame, muscleCount+7];
        currentMuscles = new float[muscleCount];

        animator = GetComponent<Animator>();
        poseHandler = new HumanPoseHandler(animator.avatar, transform);

        academyAgent = GameObject.Find("AgentAcademy").GetComponent<Academy_Agent>();
        isTraining = academyAgent.TrainingCheck();

        // Initiate dropdown options
        if(!isTraining)
        {
            recInfo = GameObject.Find("recInfo").GetComponent<Text>();
            replayInfo = GameObject.Find("replayInfo").GetComponent<Text>();
            replayFile = GameObject.Find("replayFile").GetComponent<InputField>();
            recorderName = GameObject.Find("recorder").GetComponent<InputField>();
            recStartEnd = GameObject.Find("recStartEnd").GetComponent<Button>();
            recSave = GameObject.Find("recSave").GetComponent<Button>();
            replayStartEnd = GameObject.Find("replayStartEnd").GetComponent<Button>();
            replayLoad = GameObject.Find("replayLoad").GetComponent<Button>();
            targetDropdown = GameObject.Find("target").GetComponent<Dropdown>();
            targetNumDropdown = GameObject.Find("targetNum").GetComponent<Dropdown>();
            KinectLeap = GameObject.Find("kinect").GetComponent<Toggle>();
            LeftRight = GameObject.Find("handGroup/left").GetComponent<Toggle>();
            TrainTest = GameObject.Find("train").GetComponent<Toggle>();

            targetDropdown.ClearOptions();
            targetNumDropdown.ClearOptions();
            targetDropdown.AddOptions(Enum.GetNames(typeof(Academy_Agent.targets)).ToList());
            targetNumDropdown.AddOptions(Enumerable.Range(0,10).Select(num => num.ToString()).ToList());
        }
    }

    void Update()
    {
        if(selfRecording&&!isTraining)
        {
            if(Input.GetKeyDown(KeyCode.Space))
            {
                StartEndRecording();
            }
            if(Input.GetKeyDown(KeyCode.Return))
            {
                SaveAnimation();
            }
        }
    }

    // Update is called once per frame
    // even running this in LateUpdate does not capture IK
    void LateUpdate()
    {
        if (recordPoses) { RecordPoses(); } 
        if (reapplyPoses) { reapplyPosesAnimation(); }
    }
    
    // Save entire animation as a csv file
    public void SaveAnimation()
    {
        // Retrieve current time in MM-dd-yyyy_HH:mm:ss
        DateTime now = DateTime.Now;

        string path = Directory.GetCurrentDirectory();
        path = path + "/Assets/Recordings/" + String.Join("_",new string[]{KinectLeap.isOn? "Kinect":"Leap", recorderName.text, targetDropdown.captionText.text, 
            targetNumDropdown.value.ToString(), LeftRight.isOn? "left":"right", now.ToString("MM-dd-yyyy_HH-mm-ss"), TrainTest.isOn? "train":"test"}) + ".csv";
        TextWriter sw = new StreamWriter(path);
        string line;

        for (int frame = 0; frame < counterRec; frame++) // run through all frames 
        {
            line = "";
            for (int i = 0; i < muscleCount+7; i++) // and all values composing one Pose
            {
                line = line + animationHumanPoses[frame, i].ToString() + ";";
            }
            sw.WriteLine(line);
        }
        sw.Close();

        if(!isTraining)
        {
            recInfo.text = "Animation Saved!";
            recSave.interactable = false;
            StartCoroutine(RecInfoCoroutine());
        }

        // Save target position and poses as a dictionary
        if(logPoses)
        {            
            Transform target = GameObject.Find($"Targets/{targetDropdown.captionText.text}").transform.GetChild(targetNumDropdown.value);
            Vector3 position = target.position; //target global position

            string _path = Directory.GetCurrentDirectory();
            _path = _path + "/Assets/Recordings/pose_prediction_train/" + String.Join("_", new string[]{KinectLeap.isOn? "Kinect":"Leap", recorderName.text, targetDropdown.captionText.text, 
                targetNumDropdown.value.ToString(), LeftRight.isOn? "left":"right", now.ToString("MM-dd-yyyy_HH-mm-ss"), "posepredic"}) + ".csv";
            TextWriter _sw = new StreamWriter(_path);
            string _line;

            for (int frame = 0; frame < counterRec; frame++) // run through all frames 
            {
                _line = "";
                _line = _line + position.x + ";";
                _line = _line + position.y + ";";
                _line = _line + position.z + ";";
                for (int i = 0; i < muscleCount+7; i++) // and all values composing one Pose
                {
                    _line = _line + animationHumanPoses[frame, i].ToString() + ";";
                }
                _sw.WriteLine(_line);
            }
            _sw.Close();      
        }
    }

    // Refill animationHumanPoses with values from loaded csv files
    public void LoadAnimation(string loadedFile)
    {
        string path = Directory.GetCurrentDirectory();
        path = path + "/Assets/Recordings/Archived/" + (loadedFile.EndsWith(".csv")? loadedFile:(loadedFile+".csv"));

        if (File.Exists(path))
        {
            if(!isTraining)
            {
                replayStartEnd.interactable = true;
                replayInfo.text = "File Loaded!";
            }

            StreamReader sr = new StreamReader(path);
            int frame = 0;
            string[] line;
            while (!sr.EndOfStream)
            {
                line = sr.ReadLine().Split(';');
                for (int muscleNum = 0; muscleNum < line.Length - 1; muscleNum++)
                {
                    animationHumanPoses[frame, muscleNum] = float.Parse(line[muscleNum]);
                }
                frame++;
            }
            counterLoad = frame;
        }
        else
        {
            if(!isTraining)
            {
                replayInfo.text = "File cannot be found in the directory!";
                StartCoroutine(ReplayInfoCoroutine());
            }
        }
    }

    // Load animation from the input text field. Used for recording.
    public void LoadAnimationFromTextField()
    {
        string path = Directory.GetCurrentDirectory();
        path = path + "/Assets/Recordings/Archived/" + (replayFile.text.EndsWith(".csv")? replayFile.text:(replayFile.text+".csv"));

        if (File.Exists(path))
        {
            if(!isTraining)
            {
                replayStartEnd.interactable = true;
                replayInfo.text = "File Loaded!";
            }

            StreamReader sr = new StreamReader(path);
            int frame = 0;
            string[] line;
            while (!sr.EndOfStream)
            {
                line = sr.ReadLine().Split(';');
                for (int muscleNum = 0; muscleNum < line.Length - 1; muscleNum++)
                {
                    animationHumanPoses[frame, muscleNum] = float.Parse(line[muscleNum]);
                }
                frame++;
            }
            counterLoad = frame;
        }
        else
        {
            if(!isTraining)
            {
                replayInfo.text = "File cannot be found in the directory!";
                StartCoroutine(ReplayInfoCoroutine());
            }
        }
    }

    public void StartEndRecording()
    {
        recordPoses = !recordPoses;
        if(!isTraining)
        {
            if(!recordPoses)
            {
                recSave.interactable = true;
                recInfo.text = "Record Ended!";
                recStartEnd.GetComponentInChildren<Text>().text = "Start Recording";
                StartCoroutine(RecInfoCoroutine());
            }
            else
            {
                counterRec = 0;
                recSave.interactable = false;
                replayStartEnd.interactable = false;
                recInfo.text = "Recording!";
                recStartEnd.GetComponentInChildren<Text>().text = "End Recording";
            }
        }
    }

    public void StartEndReplay()
    {
        if(recordPoses)
        {
            if(!isTraining)
            {
                replayInfo.text = "You cannot replay while recording!";
                StartCoroutine(ReplayInfoCoroutine());
            }
        }
        else
        {
            reapplyPoses = !reapplyPoses;
            if(!isTraining)
            {
                if(!reapplyPoses)
                {
                    replayLoad.interactable = true;
                    replayInfo.text = "Replay Ended!";
                    replayStartEnd.GetComponentInChildren<Text>().text = "Start Replay";
                    StartCoroutine(ReplayInfoCoroutine());
                }
                else
                {
                    counterRec = 0;
                    counterPlay = 0;
                    replayLoad.interactable = false;
                    replayInfo.text = "Replaying!";
                    replayStartEnd.GetComponentInChildren<Text>().text = "End Replay";
                }
            }
        }
    }

    // Record posed up to maximum frames
    public void RecordPoses()
    {
        poseHandler.GetHumanPose(ref currentPose);

        if (counterRec == 0)
        {
            positionAtStart = transform.position;
            rotationAtStart = transform.rotation;
            posePositionAtStart = currentPose.bodyPosition;
            poseRotationAtStart = currentPose.bodyRotation;

            animationHumanPoses[counterRec, 0] = positionAtStart.x;
            animationHumanPoses[counterRec, 1] = positionAtStart.y;
            animationHumanPoses[counterRec, 2] = positionAtStart.z;
            animationHumanPoses[counterRec, 3] = rotationAtStart.x;
            animationHumanPoses[counterRec, 4] = rotationAtStart.y;
            animationHumanPoses[counterRec, 5] = rotationAtStart.z;
            animationHumanPoses[counterRec, 6] = rotationAtStart.w;

            counterRec++;
        }
        else if (counterRec < maxFrame)
        {
            currentPosition = currentPose.bodyPosition;
            currentRotation = currentPose.bodyRotation;
            animationHumanPoses[counterRec, 0] = currentPosition.x - posePositionAtStart.x;
            animationHumanPoses[counterRec, 1] = currentPosition.y;
            animationHumanPoses[counterRec, 2] = currentPosition.z - posePositionAtStart.z;
            animationHumanPoses[counterRec, 3] = currentRotation.x;
            animationHumanPoses[counterRec, 4] = currentRotation.y;
            animationHumanPoses[counterRec, 5] = currentRotation.z;
            animationHumanPoses[counterRec, 6] = currentRotation.w;
            for (int i = 7; i < muscleCount + 7; i++) 
            {
                animationHumanPoses[counterRec, i] = currentPose.muscles[i-7];
            }
            
            counterRec++;
        }
    }

    // Loop through array and apply poses one frame after another. 
    public void reapplyPosesAnimation()
    {
        poseToSet = new HumanPose();

        int currentFrame = counterPlay%counterLoad;

        if (currentFrame == 0)
        {
            transform.position = new Vector3(animationHumanPoses[currentFrame, 0],animationHumanPoses[currentFrame, 1],animationHumanPoses[currentFrame, 2]);;
            // transform.rotation = new Quaternion(animationHumanPoses[currentFrame, 3],animationHumanPoses[currentFrame, 4],animationHumanPoses[currentFrame, 5],animationHumanPoses[currentFrame, 6]);
            transform.rotation = Quaternion.identity;

            counterPlay++;
        }
        else if (!isLoop && counterPlay >= counterLoad)
        {
            transform.position = positionAtStart;
            transform.rotation = rotationAtStart;
        }
        else
        {
            poseToSet.bodyPosition = new Vector3(animationHumanPoses[currentFrame, 0],animationHumanPoses[currentFrame, 1],animationHumanPoses[currentFrame, 2]);
            poseToSet.bodyRotation = new Quaternion(animationHumanPoses[currentFrame, 3],animationHumanPoses[currentFrame, 4],animationHumanPoses[currentFrame, 5],animationHumanPoses[currentFrame, 6]);
            for (int i = 0; i < muscleCount; i++) { currentMuscles[i] = animationHumanPoses[currentFrame, i+7]; } // somehow cannot directly modify muscle values
            poseToSet.muscles = currentMuscles;
            poseHandler.SetHumanPose(ref poseToSet);

            counterPlay++;
        }       
    }

    IEnumerator RecInfoCoroutine()
    {
        yield return new WaitForSeconds(1);
        recInfo.text = "";
    }

    IEnumerator ReplayInfoCoroutine()
    {
        yield return new WaitForSeconds(1);
        replayInfo.text = "";
    }
}

