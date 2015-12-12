using Newtonsoft.Json;
using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Linq;
using System.Runtime.CompilerServices;
using System.Text;
using System.Threading;
using System.Threading.Tasks;
using uPLibrary.Networking.M2Mqtt;
using uPLibrary.Networking.M2Mqtt.Messages;

namespace MiniMagellan
{
    public class Vision : IBehavior
    {
        MqttClient Mq;

        public bool Lock { get; set; }

        enum VisionState { Idle, Run };

        VisionState SubState = VisionState.Idle;

        enum ConeState { Lost, LostWait, Found };
        ConeState coneFlag = ConeState.Lost;

        public static float ConeConfidence = 0;

        public event EventHandler OnLostCone;
        public event EventHandler OnFoundCone;

        DateTime lastTime = DateTime.Now, coneLastSeenTime;
        TimeSpan lostWaitTime = new TimeSpan(0, 0, 2);    // 2 seconds

        // working ok, probably needs tweaking to cones and sunlight
        float kP = 0.15F, kI = 0.1F, kD = 0;
        float prevErr, integral, derivative;
        public int servoPosition = 90;
        
        public void TaskRun()
        {
            Trace.t(cc.Norm, "Vision TaskRun started");

            Program.Pilot.Send(new { Cmd = "SRVO", Value = servoPosition });

            if (!Program.PilotString.Contains("com"))
            {
                Mq = new MqttClient(Program.PilotString);
                Trace.t(cc.Norm, string.Format("vision connecting to MQTT @ {0}", Program.PilotString));
                Mq.Connect("MMPXY");
                Trace.t(cc.Norm, "vision connected");

                Mq.MqttMsgPublishReceived += PixyMqRecvd;
                Mq.Subscribe(new string[] { "robot1/pixyCam" }, new byte[] { MqttMsgBase.QOS_LEVEL_EXACTLY_ONCE });
            }

            SubState = VisionState.Run;

            // if finished, exit task
            while (Program.State != RobotState.Shutdown)
            {
                while (Lock)
                    Thread.SpinWait(100);

                if (SubState == VisionState.Run)
                    if (coneFlag != ConeState.Lost && DateTime.Now > coneLastSeenTime + lostWaitTime)
                        LostCone();

                Thread.Sleep(200);
            }

            Trace.t(cc.Warn, "Vision exiting");
            SubState = VisionState.Idle;
            if (Mq != null && Mq.IsConnected)
                Mq.Disconnect();
        }

        private void LostCone()
        {
            ConeConfidence = 0;
            coneFlag = ConeState.Lost;
            Trace.t(cc.Warn, "Cone lost"); Console.Beep(); Console.Beep();

            servoPosition = 90;
            Program.Pilot.Send(new { Cmd = "SRVO", Value = servoPosition });
            if (OnLostCone != null)
                OnLostCone(this, null);
        }

        private void FoundCone()
        {
            coneFlag = ConeState.Found;
            Trace.t(cc.Good, "Cone Found"); Console.Beep();

            prevErr = integral = derivative = 0;

            if (OnFoundCone != null)
                OnFoundCone(this, null);
        }

        void PixyMqRecvd(object sender, MqttMsgPublishEventArgs e)
        {
            //_T();
            if (SubState == VisionState.Run)
            {
                dynamic a = JsonConvert.DeserializeObject(Encoding.UTF8.GetString(e.Message));
                int middle = a.X + (a.Width / 2);      // 160 is pixy center
                DateTime nowTime = DateTime.Now;

                // lots or no rects is a good indication we dont have the cone
                // after some elapsed time of no cone, return servo to 90

                ConeConfidence = 1 / (int)a["Count"];   // todo need to consider cone size

                if (ConeConfidence < .49)
                {
                    if (coneFlag == ConeState.Found)
                        coneFlag = ConeState.LostWait;
                }
                else
                {
                    coneLastSeenTime = nowTime;

                    if (coneFlag != ConeState.Found)
                        if (coneFlag == ConeState.LostWait)
                            coneFlag = ConeState.Found;
                        else
                            FoundCone();
                    else if (coneFlag == ConeState.Found)
                    {
                        float et = (nowTime - lastTime).Milliseconds / 1000F;
                        servoPosition -= (int)Pid(160, middle, kP, kI, kD, ref prevErr, ref integral, ref derivative, et, .8F);
                        //Trace.t(cc.Norm, string.Format("Steering srvoPosition {0} e({1:F2}) i({2}) d({3})", servoPosition, prevErr, integral, derivative));
                        if (servoPosition < 0)
                            servoPosition = 0;
                        else if (servoPosition > 180)
                            servoPosition = 180;

                        Program.Pilot.Send(new { Cmd = "SRVO", Value = servoPosition });
                    }
                }

                lastTime = nowTime;
            }
        }

        public string GetStatus()
        {
            return string.Format("{0} Servo Position({1})", Enum.GetName(typeof(VisionState), SubState), servoPosition);            
        }

        float Pid(float setPoint, float presentValue, float Kp, float Ki, float Kd,
            ref float previousError, ref float integral, ref float derivative, float dt, float errorSmooth)
        {
            float error = setPoint - presentValue;
            error = (float)(errorSmooth * error + (1.0 - errorSmooth) * previousError);  // complimentry filter smoothing
            integral += error * dt;
            if (integral > 10)
                integral = 10;
            if (integral < -10)
                integral = -10;
            derivative = (error - previousError) / dt;
            derivative = derivative > 100 ? 100 : derivative < -100 ? 100 : derivative;         // constrain derivative
            float output = Kp * error + Ki * integral + Kd * derivative;
            previousError = error;
            return output;
        }
    }
}
