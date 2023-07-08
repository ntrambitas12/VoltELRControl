using System;
using System.CodeDom;
using System.Collections.Generic;
using System.Linq;
using System.Reflection;
using System.Runtime.Remoting.Channels;
using System.Text;
using System.Threading;
using System.Threading.Tasks;
using SAE.J2534;

namespace VoltELR
{
    internal class Program
    {
        static string UserInput = "";
        static object lockFlag = new object();
        static bool AlreadyRanFlag = false;
        static string ModuleSelected = string.Empty;
    
        
        public static void Main(string[] args)
        {
            Device device1;
            Channel channel1;
            Channel GMLAN;
            Dictionary<String, (String, byte[], Boolean)> CANActions = intializeCANActions();
            Dictionary<String, byte[]> CANCommands = intializeCANCommands();
            Dictionary<String, (byte[], Boolean)> CANModules = intializeCANModules();
            const int GMLANPIN = 0x01;

            // Prompt for Device
             string Device = PromptForDevice(); 
            
          
                string DllFileName = APIFactory.GetAPIinfo().First(api => api.Name.Contains(Device)).Filename;
                API API = APIFactory.GetAPI(DllFileName);

                Console.WriteLine("PTOpen");
                device1 = API.GetDevice();
                Console.WriteLine("PTReadVersion");
                Console.WriteLine(device1.FirmwareVersion);


                Console.WriteLine("Setting up connection for GMLAN");
            // Open channel for GMLAN
            GMLAN = device1.GetChannel(Protocol.SW_CAN_PS, Baud.GMLAN_33333, ConnectFlag.CAN_29BIT_ID);
            //GMLAN.SetConfig(Parameter.J1962_PINS, 0x0000);
            channel1 = device1.GetChannel(Protocol.CAN, Baud.CAN_500000, ConnectFlag.NONE);
          
               

            //create a thread that handels user input
            Thread UsrInputThread = new Thread(() => PromptForSelection(CANActions, CANCommands, CANModules));
            UsrInputThread.Start();


            //Ask user for what command to send.
            while (UserInput != "DONE")
                {
                
                    //check if its a CAN command
                    if (ModuleSelected != string.Empty) GenericSend(channel1, GMLAN, CANModules[ModuleSelected].Item1, CANCommands[UserInput], CANModules[ModuleSelected].Item2);
                   
                     else if (CANActions.ContainsKey(UserInput))
                    {
                        bool keepAlive = CANActions[UserInput].Item3;
                        bool HSCAN = CANModules[CANActions[UserInput].Item1].Item2;
                        byte[] ModuleID = CANModules[CANActions[UserInput].Item1].Item1;
                        byte[] Payload = CANActions[UserInput].Item2;

                        /*Logic below ensures that CAN commands that should only run once per user command*/
                        if (!AlreadyRanFlag)
                        {
                            GenericSend(channel1, GMLAN, ModuleID, Payload, HSCAN);
                     
                            lock (lockFlag)
                            {
                                //Set the AlreadyRanFlag
                                AlreadyRanFlag = true;
                            }
                        }

                     //if keepAliveFlag is set, keep alive
                        if (keepAlive)
                        {
                        Payload = CANCommands["KeepAlive"];
                        //Send keepAlive command
                        GenericSend(channel1, GMLAN, ModuleID, Payload, HSCAN);
                        }
                    } 
                    
                }

             UsrInputThread.Join();
                Thread.Sleep(2000);
                Console.WriteLine("PTDisconnect");
                channel1.Dispose();
                GMLAN.Dispose();
                Console.WriteLine("PTClose");
                device1.Dispose();
      
        }



        /*
             This function will allow the use of customizing a generic CAN message/command
             to be reused for different modules 
        */
        private static void GenericSend(Channel HSChannel, Channel GMLAN,  byte[] moduleID, byte[] genericCommand, Boolean isHS)
        {
            //create packet consisting of ModuleID and Payload
            byte[] Payload = new byte[moduleID.Length + genericCommand.Length];
            moduleID.CopyTo(Payload, 0);
            genericCommand.CopyTo(Payload, moduleID.Length);

            //send the packet
           // if (isHS) {
                HSChannel.SendMessage(new Message(Payload, TxFlag.NONE));
           // }
          /*  else
            {
                GMLAN.SendMessage(new Message(Payload, TxFlag.CAN_29BIT_ID));
            } */

            Thread.Sleep(500);
        }





        private static void PromptForSelection( Dictionary<String, (String, byte[], Boolean)> CANActions, Dictionary<String, byte[]> CANCommands, Dictionary<String, (byte[], Boolean)> CANModules)
        {
            while (UserInput != "DONE")
            {
                

                Console.WriteLine("\nPlease enter a command (case sensative) to execute.");
                Console.WriteLine("To exit program, enter \"DONE\".\n");


                //print out all the available commands to the user
                foreach (KeyValuePair<String, (String, byte[], Boolean )> CANAction in CANActions)
                {
                    Console.WriteLine(CANAction.Key);
                }

                foreach(KeyValuePair<String, byte[]> CANCommand in CANCommands)
                {
                    Console.WriteLine(CANCommand.Key);
                }

                
                String newUsrInput = Console.ReadLine();

                lock (lockFlag)
                {
                    //reset ranOnce flag after reading new user input
                    AlreadyRanFlag = false;
                    //if input is a CAN command, get the module to execute the command against. Otherwise empty the string
                    ModuleSelected = (CANCommands.ContainsKey(newUsrInput)) ? PromptModule(CANModules) : string.Empty;
                    UserInput = newUsrInput;
                    
                }
            }
         
        }

        private static String PromptModule(Dictionary<String, (byte[], Boolean)> CANModules)
        {
            Console.WriteLine("\nSelect a module from below to execute the command on: \n");
            
            foreach (KeyValuePair<String, (byte[], Boolean)> Module in CANModules)
            {
                Console.WriteLine(Module.Key);
            }

            return Console.ReadLine();
        }

        private static String PromptForDevice()
        {
            Console.WriteLine("Hi, welcome to the VOLTEC Utility.");
            Console.WriteLine("This toolset is in BETA and is to be used as an engineering tool.");
            Console.WriteLine("This tool is designed specfically for the GEN1 Chevy Volt and the Cadillac ELR");
            Console.WriteLine("This tool might work for other GM products but has not been tested. Use at your own risk.");
            Console.WriteLine("Author is NOT liable for any damadge done to your vehicle under any circumstances!");
            Console.WriteLine("Enter the J2534 Device that will be used to connect to the vehicle.");
            Console.WriteLine("Current accepted values are \"MDI\" and  \"VXDIAG\"");
            return Console.ReadLine();
        }
        

       private static Dictionary<String, (String, byte[], Boolean)> intializeCANActions()
        {
            /*
             Structure of CAN Command Dictionary:
               Key:String -- The Name of the command, shown to the user in a form of a prompt
               Values: Tuple(Byte[], Boolean, Boolean) == String-> Name of module to direct message to (looked up in a seperate table)

                                                          == Byte[] -> byte array represents payload

                                                          
                                                          == Boolean -> represents flag if Command should keep Alive
                                                                        KeepAlive = True, Run Once = False
             */
           
            Dictionary<String, (String, byte[],  Boolean)> CANActions = new Dictionary<String, (String, byte[], Boolean)>();


            //Populate dictionary with different CAN Actions
            CANActions.Add("PassWindowDown", ("BCM", new byte[] { 0x07, 0xae, 0x3b, 0x02, 0x00, 0x01, 0x00, 0x00 }, false));
            CANActions.Add("PassWindowUp", ("BCM", new byte[] { 0x07, 0xae, 0x3b, 0x02, 0x00, 0x02, 0x00, 0x00 }, false));
            CANActions.Add("DriverWindowDown", ("BCM", new byte[] { 0x07, 0xae, 0x3b, 0x01, 0x01, 0x00, 0x00, 0x00 }, false));
            CANActions.Add("DriverWindowUp", ("BCM", new byte[] { 0x07, 0xae, 0x3b, 0x01, 0x02, 0x00, 0x00, 0x00 }, false));
            CANActions.Add("TurnSignalOn", ("BCM", new byte[] { 0x07, 0xae, 0x02, 0xf0, 0xf0, 0x00, 0x00, 0x00 }, true));
            CANActions.Add("TurnSignalOff", ("BCM", new byte[] { 0x07, 0xae, 0x02, 0xf0, 0x00, 0x00, 0x00, 0x00 }, false));
            CANActions.Add("InterLEDOff", ("BCM", new byte[] { 0x07, 0xae, 0x09, 0x02, 0x00, 0x00, 0x00, 0x00 }, true));
            CANActions.Add("InterLEDOn", ("BCM", new byte[] { 0x07, 0xae, 0x09, 0x02, 0x00, 0x00, 0x7f, 0xff }, true));

            CANActions.Add("ChargeLEDOff", ("BCM", new byte[] { 0x07, 0xae, 0x14, 0x00, 0x00, 0x02, 0x00, 0x00 }, true));
            CANActions.Add("ChargeLEDOn", ("BCM", new byte[] { 0x07, 0xae, 0x14, 0x00, 0x00, 0x02, 0x00, 0x02 }, true));

   

            CANActions.Add("HighbeamsOn", ("BCM", new byte[] { 0x07, 0xae, 0x02, 0x00, 0x00, 0x02, 0x02, 0x00 }, true));
            CANActions.Add("HighbeamsOff", ("BCM", new byte[] { 0x07, 0xae, 0x02, 0x00, 0x00, 0x02, 0x00, 0x00 }, false));
            CANActions.Add("RemoteStart", ("ONSTAR", new byte[] { 0x80, 0x01, 0xFF }, false));
            CANActions.Add("WiperHigh", ("BCM", new byte[] { 0x07, 0xae, 0x03, 0x80, 0x00, 0x04, 0x00, 0x00 }, true));
            CANActions.Add("StartEngine", ("Hybrid1", new byte[] { 0x07, 0xae, 0x31, 0x06, 0x00, 0x00, 0x00, 0x00 }, true));
            CANActions.Add("StopEngine", ("Hybrid1", new byte[] { 0x07, 0xae, 0x31, 0x05, 0x00, 0x00, 0x00, 0x00 }, true));
            CANActions.Add("EngineFanOn", ("Hybrid2", new byte[] {  0x07, 0xae, 0x3b, 0x00, 0x00, 0x01, 0x4c, 0xcc }, true));
            CANActions.Add("BattCoolantPump", ("Hybrid2", new byte[] { 0x07, 0xae, 0x40, 0x40, 0x4c, 0xcc, 0x00, 0x00 }, true));

            return CANActions;
        }

        private static Dictionary<String, byte[]> intializeCANCommands()
        {
            Dictionary<String, byte[]> CANCommands = new Dictionary<String, byte[]>();
           
            //Populate dictionary with different CAN Commands
            CANCommands.Add("RelearnVIN", new byte[] { 0x07, 0xae, 0x2a, 0x80, 0x00, 0x00, 0x00, 0x00 });
            CANCommands.Add("Release", new byte[] { 0x02, 0xae, 0x00, 0x00, 0x00, 0x00, 0x00, 0x00});
            CANCommands.Add("KeepAlive", new byte[] { 0x01, 0x3e, 0x00, 0x00, 0x00, 0x00, 0x00, 0x00 });
    
            return CANCommands;
        }

        private static Dictionary<String, (byte[], Boolean)> intializeCANModules()
        {
            /*
             Params: 
                Key: String -> The name of the module
                Values: byte[] -> The address of the module
                        Boolean -> Indicats HSCAN or GMLAN (True, False respectivly)
             */
            Dictionary<String, (byte[], Boolean)> CANModules = new Dictionary<String, (byte[], Boolean)>();

            //Populate with different CAN Modules

            CANModules.Add("HMI", (new byte[] {0x00, 0x00, 0x02, 0x52}, false));
            CANModules.Add("Radio", (new byte[] { 0x00, 0x00, 0x02, 0x44 }, false));
            CANModules.Add("Hybrid1", (new byte[] {0x00, 0x00, 0x07, 0xe1}, true));
            CANModules.Add("Hybrid2", (new byte[] { 0x00, 0x00, 0x07, 0xe4 }, true));
            CANModules.Add("BCM", (new byte[] { 0x00, 0x00, 0x02, 0x41 }, false));
            CANModules.Add("ONSTAR", (new byte[] {0x10, 0x24, 0xe0, 0x97}, false));

            return CANModules;  
        }


    }
}
