import argparse
from datetime import datetime

def main():
    parser = argparse.ArgumentParser('Writes Appcast XML file')
    parser.add_argument('-v', '--version', help='Version number', required=True)
    parser.add_argument('-o', '--os', help='OS name', required=True)
    parser.add_argument('-r', '--rid', help='RID', required=True)
    parser.add_argument('-f', '--file', help='File name', required=True)
    parser.add_argument('-t', '--tag', help='Tag (Version if unspecified)')
    args = parser.parse_args()

    appcast_ver = args.version
    appcast_os = args.os
    appcast_rid = args.rid
    appcast_file = args.file
    appcast_tag = args.tag or args.version

    xml = '''<?xml version="1.0" encoding="utf-8"?>
<rss version="2.0" xmlns:sparkle="http://www.andymatuschak.org/xml-namespaces/sparkle">
<channel>
    <title>OpenUtau</title>
    <language>en</language>
    <item>
    <title>OpenUtau %s</title>
    <pubDate>%s</pubDate>
    <enclosure url="https://github.com/stakira/OpenUtau/releases/download/%s/%s"
                sparkle:version="%s"
                sparkle:shortVersionString="%s"
                sparkle:os="%s"
                type="application/octet-stream"
                sparkle:signature="" />
    </item>
</channel>
</rss>''' % (appcast_ver, datetime.now().strftime("%a, %d %b %Y %H:%M:%S %z"),
             appcast_tag, appcast_file, appcast_ver, appcast_ver, appcast_os)

    with open("appcast.%s.xml" % (appcast_rid), 'w') as f:
        f.write(xml)


if __name__ == '__main__':
    main()
