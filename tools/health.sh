#!/bin/bash
exec 3<>/dev/tcp/127.0.0.1/8084 || exit 1
printf 'GET /ready HTTP/1.0\r\nHost: localhost\r\n\r\n' >&3
cat <&3 | grep -q '"status":"UP"'
