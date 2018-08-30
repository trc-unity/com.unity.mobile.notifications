#!/usr/bin/env bash
set -xeuo pipefail

cd com.unity.android.notifications
ls
ls Runtime/Plugins/Android/

if [ -f Runtime/Plugins/Android/androidnotifications-release.aar ];
then
    echo "Built AAR package found in Plugins folder."
else
	echo "Built AAR package not found in Plugins folder!"
    exit 1
fi

if [ -f Documentation\~/html/index.html ];
then
    echo "Generated html documentation found."
else
	echo "Generated html documentation not found!"
    exit 1
fi

ls Documentation\~/html

curl -u $BIN_USERNAME@unity:$BIN_API_KEY https://packages.unity.com/auth > .npmrc
sed -i -e 's/npm\/unity\/unity/npm\/unity\/unity-staging/g' .npmrc
npm publish