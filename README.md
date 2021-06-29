# adless-dns
## Introduction
Using Windows' hosts file to handle ads is annoying because there's way too many dns records that have to be overriden and it takes a while for Windows to process such a hosts file. To go around this you can use a self-hosted dns server. You may overwrite the hosts file and add any other records you may want to redirect to another ip. Currently we only handle DNS A records.

## Setup
Once you create the Windows service or the Linux systemd service you will have to configure your DNS settings to use the service you just created. Be careful to edit both IPv4 and IPv6 settings as Windows will default to using IPv6 if it is available.

## Enjoy