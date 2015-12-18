from pixy import *
from ctypes import *
import time
#from pudb import set_trace; set_trace()

import paho.mqtt.client as mqtt
import serial, time, json, os, inspect, re, subprocess
from threading import Thread
from subprocess import Popen

def on_connect(client, userdata, flags, rc):
    print("MQTT connected\r")
    #client.subscribe("robot1/Cmd/#")

    def on_message(client, userdata, msg):
        print(msg.topic + " " + str(msg.payload).strip() +"\r")
        Serial.write(str(msg.payload).strip() + "\n")


        class Blocks (Structure):
            _fields_ = [ ("type", c_uint),
            ("signature", c_uint),
            ("x", c_uint),
            ("y", c_uint),
            ("width", c_uint),
            ("height", c_uint),
            ("angle", c_uint) ]

#-----------------------------------------------------------------

print ("pyPixy - spiked3.com ... running")
pixy_init()

while True:

    blocks = BlockArray(512)

    client = mqtt.Client()
    client.on_connect = on_connect

    client.connect_async("127.0.0.1")
    client.loop_start()

    while 1:

        count = pixy_get_blocks(512, blocks)
        if count > 0:
            rectList = []
            for i in range(0, count):
                #filter only desired type and signature, change to whichever is appropriate
                if blocks[i].type == 0 and blocks[i].signature == 1:
                    #add to list
                    rectList.append( blocks[i] )

            if len(rectList) > 0:
                #find larget rectangle in list
                max_val = max(rectList, key = lambda x:x.width * x.height)          
                #publish it
                a = { 'Count' : len(rectList), 'Signature' : max_val.signature, 'Size' : max_val.width * max_val.height, 'Center' : max_val.x + (max_val.width/2) }
                print a
                client.publish('robot1/pixyCam', bytearray(json.dumps(a)))

        #throttle
        time.sleep(.1)
