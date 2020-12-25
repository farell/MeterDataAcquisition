using System;
using System.Collections.Generic;
using System.IO;
using System.IO.Ports;
using System.Runtime.Remoting.Metadata.W3cXsd2001;
using System.Linq;
using System.Text;
using System.Threading.Tasks;
using System.Timers;
using System.Windows.Forms;
using Newtonsoft.Json;
using StackExchange.Redis;

namespace SattiliteDataAcquisition
{
    class MeterCom
    {
        private SerialPort comPort;
        private Form1 window;
        private string id;
        private string portName;
        private byte[] buffer;

        private double workingConditionFlowAccum;
        private double standardConditionFlowAccum;
        private double workingConditionFlow;
        private double standardConditionFlow;
        private double temperature;
        private double pressure;
        private ConnectionMultiplexer redis;

        //
        public MeterCom(SerialPortConfig config, ConnectionMultiplexer redis, Form1 form1)
        {
            comPort = new SerialPort(config.portName, config.baudrate, config.parityCheck, config.databits, config.stopbits);
            this.redis = redis;
            comPort.DataReceived += new SerialDataReceivedEventHandler(ComDataReceive);
            this.buffer = new byte[1024];
            this.window = form1;
            this.portName = config.portName;
            this.id = config.id;
        }

        public void Start()
        {
            if (comPort != null)
            {
                if (!comPort.IsOpen)
                {
                    comPort.Open();
                }
            }
            
        }

        public void Stop()
        {
            if (comPort != null)
            {
                if (comPort.IsOpen)
                {
                    comPort.Close();
                }
            }
        }

        private void MinuteTimer_TimesUp(object sender, System.Timers.ElapsedEventArgs e)
        {
            if (comPort.IsOpen)
            {
                byte[] cmd = { 0x17, 0x03, 0x00, 0x0A, 0x00, 0x15, 0xA6, 0xF1 };
                comPort.Write(cmd, 0, cmd.Length);
            }
        }

        private void ComDataReceive(object sender, SerialDataReceivedEventArgs e)
        {
            int workingConditionFlowAccumIndex = 8;
            int standardConditionFlowAccumIndex = 0;
            int workingConditionFlowIndex = 4;
            int standardConditionFlowIndex = 0;
            int temperatureIndex = 8;
            int pressureIndex = 12;
            int stateIndex = 41;
            int alarmIndex = 43;

            int bytesRead = 0;
            try
            {
                bytesRead = this.comPort.Read(this.buffer, 0, 1024);
            }
            catch(Exception ex)
            {
                window.AppendLog(ex.Message);
                return;
            }

            string str = System.Text.Encoding.ASCII.GetString(this.buffer,0,bytesRead);
            char[] chs = { ',' };
            char[] ch = { ':' };
            string[] splited = str.Split(chs);
            string stamp = splited[0];
            string voltage = splited[1].Split(ch)[1];
            string m0 = splited[4].Split(ch)[1];//time
            string m1 = splited[5].Split(ch)[1];
            string m2 = splited[6].Split(ch)[1];
            string m3 = splited[7].Split(ch)[1];
            string m4 = splited[8].Split(ch)[1];

            byte[] m0_byte = GetStringToBytes(m0);
            byte[] m1_byte = GetStringToBytes(m1);
            byte[] m2_byte = GetStringToBytes(m2);
            byte[] m3_byte = GetStringToBytes(m3);
            byte[] m4_byte = GetStringToBytes(m4);

            standardConditionFlowAccum = Process8Bytes(m0_byte, standardConditionFlowAccumIndex);
            workingConditionFlowAccum = Process8Bytes(m0_byte, workingConditionFlowAccumIndex);

            standardConditionFlow = Process4Bytes(m1_byte, standardConditionFlowIndex);
            workingConditionFlow = Process4Bytes(m1_byte, workingConditionFlowIndex);
            
            temperature = Process4Bytes(m1_byte, temperatureIndex);
            pressure = Process4Bytes(m1_byte, pressureIndex);

            string state_gas_volum_shortage = (m2_byte[1]&0x10) == 0x10 ? "购气量不足" : "购气量正常";
            string state_gas_over_use = (m2_byte[1] & 0x08) == 0x08 ? "燃气使用量透支" : "燃气使用量无透支";
            string state_communication = (m2_byte[1] & 0x04) == 0x04 ? "通讯异常": "通讯正常";
            int state_valve_open = (m2_byte[1] & 0x03);// == 0x03 ? 1 : 0;

            string valve_state = "未知";

            if(state_valve_open == 0)
            {
                valve_state = "关闭";
            }else if(state_valve_open == 1)
            {
                valve_state = "打开";
            }
            else if(state_valve_open == 3)
            {
                valve_state = "异常";
            }
            else
            {
                valve_state = "未知";
            }
            
            string alarm_external_power_lose = (m2_byte[2] & 0x04) == 0x04 ? "外电源断开": "正常";
            string alarm_calculate_battery_drain = (m2_byte[2] & 0x02) == 0x02 ? "更换积算仪电池" :"正常";
            string alarm_control_battery_drain = (m2_byte[2] & 0x01) == 0x01 ? "更换卡控电池" : "正常";
            
            string alarm_tempreture_sensor_failed = (m2_byte[3] & 0x80) == 0x80 ? "温度传感器故障" : "正常";
            string alarm_pressure_sensor_failed = (m2_byte[3] & 0x40) == 0x40 ? "压力传感器故障" : "正常";
            string alarm_valve_failed = (m2_byte[3] & 0x20) == 0x20 ? "阀门故障" : "正常";
            string alarm_control_low_power = (m2_byte[3] & 0x10) == 0x10 ? "卡控电量不足" : "正常";
            string alarm_calculate_low_power = (m2_byte[3] & 0x08) == 0x08 ? "积算仪电量不足" : "正常";
            string alarm_pressure_too_high = (m2_byte[3] & 0x04) == 0x04 ? "压力超上限" : "正常";
            string alarm_tempreture_too_high = (m2_byte[3] & 0x02) == 0x02 ? "温度超上限" : "正常";
            string alarm_instant_working_condition_flow_exceed = (m2_byte[3] & 0x01) == 0x01 ? "瞬时工况流量超上限" : "正常";

            MeterStateInfo meterStateInfo = new MeterStateInfo();
            meterStateInfo.Id = id;
            meterStateInfo.Stamp = DateTime.Now.ToString("yyyy-MM-dd HH:mm:ss");
            meterStateInfo.AlarmCalculateBatteryDrain = alarm_calculate_battery_drain;
            meterStateInfo.AlarmCalculateUnderVolt = alarm_calculate_low_power;
            meterStateInfo.AlarmExternalPowerLose = alarm_external_power_lose;
            meterStateInfo.AlarmIControlBatteryDrain = alarm_control_battery_drain;
            meterStateInfo.AlarmIControlUnderVolt = alarm_control_low_power;
            meterStateInfo.AlarmInstantWorkCondExceedHighL = alarm_instant_working_condition_flow_exceed;
            meterStateInfo.AlarmPressureExceedHighL = alarm_pressure_too_high;
            meterStateInfo.AlarmPressureSensorFault = alarm_pressure_sensor_failed;
            meterStateInfo.AlarmTempExceedHighL = alarm_tempreture_too_high;
            meterStateInfo.AlarmTempSensorFault = alarm_tempreture_sensor_failed;
            meterStateInfo.AlarmValveFault = alarm_valve_failed;
            meterStateInfo.StateCommunicate = state_communication;
            meterStateInfo.StateGasOverUsed = state_gas_over_use;
            meterStateInfo.StateGasVolumShortage = state_gas_volum_shortage;
            meterStateInfo.StateValveOpen = valve_state;
            meterStateInfo.StandardConditionFlow = Math.Round(standardConditionFlow, 2);
            meterStateInfo.StandardConditionFlowAccum = Math.Round(standardConditionFlowAccum, 4);
            meterStateInfo.WorkingConditionFlow = Math.Round(workingConditionFlow, 2);
            meterStateInfo.WorkingConditionFlowAccum = Math.Round(workingConditionFlowAccum, 4);
            meterStateInfo.BatteryVoltage = Math.Round(Double.Parse(voltage)/1000, 3);
            meterStateInfo.GasPressure = Math.Round(pressure,2);
            meterStateInfo.GasTemperature = Math.Round(temperature, 2);

            string json = JsonConvert.SerializeObject(meterStateInfo);
            if (this.redis.IsConnected)
            {
                IDatabase db = this.redis.GetDatabase(1);
                db.StringSet(id, json);
            }
            
            string result = DateTime.Now.ToString()+"  "+portName + " :\r\n";
            result += "-Redis Server not connected\r\n";
            result += "-电池电压 : " + int.Parse(voltage)/1000.0 + " V\r\n";
            result += "-工况总累积量 : " + workingConditionFlowAccum + "\r\n";
            result += "-工况瞬时流量 :" + workingConditionFlow + "\r\n";
            result += "-标况累积流量 :" + standardConditionFlowAccum + "\r\n";
            result += "-标况瞬时流量 :" + standardConditionFlow + "\r\n";
            result += "-燃气温度 :" + temperature + "\r\n";
            result += "-燃气绝对压力 :" + pressure + "\r\n";

            DateTime date = DateTime.Now.Date;
            PlotData("工况瞬时流量", workingConditionFlow, date);
            PlotData("标况瞬时流量", standardConditionFlow, date);
            PlotData("燃气温度", temperature, date);
            PlotData("燃气绝对压力", pressure, date);

            result += "-购气提示状态 : " + state_gas_volum_shortage + "\r\n";
            result += "-透支状态 :" + state_gas_over_use + "\r\n";
            result += "-通讯状态（卡控与积算仪之间）:" + state_communication + "\r\n";
            result += "-阀门状态 :" + valve_state + "\r\n";

            result += "-温度传感器故障 :" + alarm_tempreture_sensor_failed + "\r\n";
            result += "-压力传感器故障 :" + alarm_pressure_sensor_failed + "\r\n";
            result += "-阀门故障 : " + alarm_valve_failed + "\r\n";
            result += "-卡控电量不足 : " + alarm_control_low_power + "\r\n";
            result += "-积算仪电量不足 :" + alarm_calculate_low_power + "\r\n";
            result += "-压力超上限报警 :" + alarm_pressure_too_high + "\r\n";
            result += "-温度超上限报警 : " + alarm_tempreture_too_high + "\r\n";
            result += "-瞬时工况超流量上限报警 :" + alarm_instant_working_condition_flow_exceed + "\r\n";
            result += "-外电源失电 :" + alarm_external_power_lose + "\r\n";
            result += "-更换积算仪电池 : " + alarm_calculate_battery_drain + "\r\n";
            result += "-更换卡控电池 : " + alarm_control_battery_drain + "\r\n";

            result += "json: "+ json + "\r\n";


            //foreach (var item in splited)
            {
                this.window.AppendLog(result);
            }
        }

        private void PlotData(string name,double data,DateTime date)
        {
            window.AppendChartData(name, data, date);
        }

        private double Process4Bytes(byte[] buffer, int index)
        {
            byte[] temp = new byte[4];
            Buffer.BlockCopy(buffer, index, temp, 0, 4);
            if (BitConverter.IsLittleEndian)
            {
                Array.Reverse(temp);
            }
            Single value = BitConverter.ToSingle(temp, 0);
            return (double)value;
        }

        private double Process8Bytes(byte[] buffer, int index)
        {
            byte[] temp = new byte[8];
            Buffer.BlockCopy(buffer, index, temp, 0, 8);
            if (BitConverter.IsLittleEndian)
            {
                Array.Reverse(temp);
            }
            double value = BitConverter.ToDouble(temp, 0);
            return value;
        }

        public static byte[] GetStringToBytes(string value)
        {
            SoapHexBinary shb = SoapHexBinary.Parse(value);
            return shb.Value;
        }

        public static string GetBytesToString(byte[] value)
        {
            SoapHexBinary shb = new SoapHexBinary(value);
            return shb.ToString();
        }
}

    class MeterStateInfo
    {
        public string Id { get; set; }
        public string Stamp { get; set; }
        public double BatteryVoltage { get; set; }
        public double WorkingConditionFlowAccum { get; set; }
        public double WorkingConditionFlow { get; set; }
        public double StandardConditionFlowAccum { get; set; }
        public double StandardConditionFlow { get; set; }
        public double GasTemperature { get; set; }
        public double GasPressure { get; set; }

        public string StateGasVolumShortage { get; set; }
        public string StateGasOverUsed { get; set; }
        public string StateCommunicate { get; set; }
        public string StateValveOpen { get; set; }

        public string AlarmTempSensorFault { get; set; }
        public string AlarmPressureSensorFault { get; set; }
        public string AlarmValveFault { get; set; }
        public string AlarmIControlUnderVolt { get; set; }
        public string AlarmCalculateUnderVolt { get; set; }
        public string AlarmPressureExceedHighL { get; set; }
        public string AlarmTempExceedHighL { get; set; }
        public string AlarmInstantWorkCondExceedHighL { get; set; }
        public string AlarmExternalPowerLose { get; set; }
        public string AlarmCalculateBatteryDrain { get; set; }
        public string AlarmIControlBatteryDrain { get; set; }
    }

    class SerialPortConfig
    {
        public string portName;
        public int baudrate;
        public Parity parityCheck;
        public int databits;
        public StopBits stopbits;
        public string id;

        public SerialPortConfig(string port, string baudrate, string parity, string databits, string sb,string id)
        {
            this.portName = port;
            this.baudrate = Int32.Parse(baudrate);
            this.databits = Int32.Parse(databits);
            this.id = id;
            switch (parity)
            {
                case "None":
                    parityCheck = Parity.None;
                    break;
                case "Odd":
                    parityCheck = Parity.Odd;
                    break;
                case "Even":
                    parityCheck = Parity.Even;
                    break;
                case "Mark":
                    parityCheck = Parity.Mark;
                    break;
                case "Space":
                    parityCheck = Parity.Space;
                    break;
                default:
                    parityCheck = Parity.None;
                    break;
            }

            switch (sb)
            {
                case "1":
                    stopbits = StopBits.One;
                    break;
                case "1.5":
                    stopbits = StopBits.OnePointFive;
                    break;
                case "2":
                    stopbits = StopBits.Two;
                    break;
                default:
                    stopbits = StopBits.One;
                    break;
            }
        }
    }
}
