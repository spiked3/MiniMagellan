#include <stdio.h>
#include <stdlib.h>
#include <unistd.h>
#include <signal.h>
#include <string.h>
#include "pixy.h"

#define BLOCK_BUFFER_SIZE    25

struct Block blocks[BLOCK_BUFFER_SIZE];

static bool run_flag = true;

void handle_SIGINT(int unused)
{
  run_flag = false;
}

#define MIN_AREA 120 * 120

void printBlock(Block& b)
{
  int i, j;
  char sig[6], d;
  bool flag;
  if ((b.width * b.height) > MIN_AREA) {
    if (b.type==PIXY_BLOCKTYPE_COLOR_CODE) {
      for (i=12, j=0, flag=false; i>=0; i-=3) {
        d = (b.signature>>i)&0x07;
        if (d>0 && !flag)
          flag = true;
        if (flag)
          sig[j++] = d + '0';
      }
      sig[j] = '\0';  
      printf("{\"ColorCode\": %d, \"x\": %d, \"y\": %d, \"width\": %d, \"height\": %d, \"angle\": %d }\n", 
        b.signature, b.x, b.y, b.width, b.height, b.angle);
    }
    else 
      printf("{\"Signature\": %d, \"x\": %d, \"y\": %d, \"width\": %d, \"height\": %d }\n", 
        b.signature, b.x, b.y, b.width, b.height);   
  }
}


int main(int argc, char * argv[])
{
  int      i = 0;
  int      index;
  int      blocks_copied;
  int      pixy_init_status;
  char     buf[128];

  signal(SIGINT, handle_SIGINT);

  printf("{\"Msg\": \"Hello Pixy Modified : libpixyusb Version: %s\" }\n", __LIBPIXY_VERSION__);

  pixy_init_status = pixy_init();

  if(!pixy_init_status == 0)
  {
    printf("{\"Error\": \"pixy_init(): ");
    pixy_error(pixy_init_status);
    printf("\" }\n");
    return pixy_init_status;
  }

#if 0
  // Request Pixy Firmware Version //
  {
    uint16_t major;
    uint16_t minor;
    uint16_t build;
    int      return_value;

    return_value = pixy_get_firmware_version(&major, &minor, &build);

    if (return_value) {
      // Error //
      printf("Failed to retrieve Pixy firmware version. ");
      pixy_error(return_value);

      return return_value;
    } else {
      // Success //
      printf(" Pixy Firmware Version: %d.%d.%d\n", major, minor, build);
    }
  }
#endif

  while(run_flag)
  {
    // Wait for new blocks to be available //
    while(!pixy_blocks_are_new() && run_flag); 

    // Get blocks from Pixy //
    blocks_copied = pixy_get_blocks(BLOCK_BUFFER_SIZE, &blocks[0]);

    if(blocks_copied < 0) {
      printf("{\"Error\": \"pixy_get_blocks(): ");
      pixy_error(blocks_copied);
      printf("\" }\n");
    }

    printf("{\"Frame\": %d }\n", i);
    for(index = 0; index != blocks_copied; ++index) {    
     printBlock(blocks[index]);     
   }
   i++;
 }
 pixy_close();
}

#if 0
  // Pixy Command Examples //
  {
    int32_t response;
    int     return_value;

    // Execute remote procedure call "cam_setAWB" with one output (host->pixy) parameter (Value = 1)
    //
    //   Parameters:                 Notes:
    //
    //   pixy_command("cam_setAWB",  String identifier for remote procedure
    //                        0x01,  Length (in bytes) of first output parameter
    //                           1,  Value of first output parameter
    //                           0,  Parameter list seperator token (See value of: END_OUT_ARGS)
    //                   &response,  Pointer to memory address for return value from remote procedure call
    //                           0); Parameter list seperator token (See value of: END_IN_ARGS)
    //

    // Enable auto white balance //
    pixy_command("cam_setAWB", UINT8(0x01), END_OUT_ARGS,  &response, END_IN_ARGS);

    // Execute remote procedure call "cam_getAWB" with no output (host->pixy) parameters
    //
    //   Parameters:                 Notes:
    //
    //   pixy_command("cam_setAWB",  String identifier for remote procedure
    //                           0,  Parameter list seperator token (See value of: END_OUT_ARGS)
    //                   &response,  Pointer to memory address for return value from remote procedure call
    //                           0); Parameter list seperator token (See value of: END_IN_ARGS)
    //

    // Get auto white balance //
    return_value = pixy_command("cam_getAWB", END_OUT_ARGS, &response, END_IN_ARGS);

    // Set auto white balance back to disabled //
    pixy_command("cam_setAWB", UINT8(0x00), END_OUT_ARGS,  &response, END_IN_ARGS);
  }
#endif
