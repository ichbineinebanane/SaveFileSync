# SaveFileSync
Simple Service to sync savegames with friends using an Amazon Web Services(AWS) Elastic Cloud Compute(EC2) instance.

The service looks if the process is either started or stopped. If opened the service checks if the the EC2 instance has more recent savegames and if so downloads them. And of course it works the same way the other way around.

If the host of the game stops playing and you want to continue playing, you will have to restart the game after he has closed it.

## Disclamer
Download speeds vary with especially free instances, so it might take a while to download or upload especially large files. Be aware that this might be an issue.

## Build
Simply open the project in Visual Studio Code(VSCode) and enter the following command.

'''dotnet publish -r win-x64 -p:PublishSingleFile=true --self-contained false'''

## Configuration
Before installing the service you have to fill out the included config.cfg with the following parameters.

### LocalDirectory 
The location on you local machine where the savegames are stored.

### Hostname
The Hostname of the EC2 instance.

### Username
The username for SSH access to your EC2 instance.

### Keyfile
The keyfile for SSH access to your EC2 instance.

## Installing the service
All of the following commands have to be entered in a command prompt with administrator privileges.
Install the service.
'''sc create SaveFileSync binPath="<binPath>"'''
Configure the service to start automaticallya.
'''sc config SaveFileSync start= auto''' 
Start the service on your own.
'''sc start SaveFileSync'''
