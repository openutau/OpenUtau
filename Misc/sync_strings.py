import codecs
import os
import sys
import xml.etree.ElementTree as ET

# Element tags are written back with the prefix their namespace is declared
# with, so a typed resource such as system:Double stays valid XAML.
PREFIXES = {
    'https://github.com/avaloniaui': '',
    'clr-namespace:System;assembly=mscorlib': 'system:',
    'http://schemas.microsoft.com/winfx/2006/xaml': 'x:',
}
TEXT = 'system:String'


def tag_name(tag):
    if not tag.startswith('{'):
        return tag
    ns, _, local = tag[1:].partition('}')
    return PREFIXES.get(ns, '') + local


def register_all_namespaces(filename):
    namespaces = dict(
        [node for _, node in ET.iterparse(filename, events=['start-ns'])])
    for ns in namespaces:
        ET.register_namespace(ns, namespaces[ns])


def file_to_dict(filename):
    with open(filename, "r", encoding='utf8') as f:
        src_etree = ET.parse(f)
    return {
        child.get('{http://schemas.microsoft.com/winfx/2006/xaml}Key'):
        (tag_name(child.tag), child.text)
        for child in src_etree.getroot()[:]
        if (child.text != "" and child.text is not None)
    }


def dict_to_file(filename, dict, en_dict):
    # utf-8-sig keeps the byte order mark that Strings.axaml is stored with.
    with open(filename, "rb") as f:
        encoding = "utf-8-sig" if f.read(3) == codecs.BOM_UTF8 else "utf8"
    with open(filename, "w", encoding=encoding) as f:
        if(not filename.endswith("Strings.axaml")):
            f.write(
                '<!--UI strings. Entries commented out are untranslated and fall back to en-US.-->\n')
        f.write('<ResourceDictionary xmlns="https://github.com/avaloniaui"\n')
        f.write(
            '                    xmlns:system="clr-namespace:System;assembly=mscorlib"\n')
        f.write(
            '                    xmlns:x="http://schemas.microsoft.com/winfx/2006/xaml">\n')
        last_section = None
        for key in sorted(dict.keys()):
            try:
                section = key[:key.index('.')]
            except ValueError:
                section = key
            if last_section != section:
                f.write('\n')
            last_section = section
            tag = en_dict[key][0] if en_dict else dict[key][0]
            text = dict[key][1]
            line = '<%s x:Key="%s">%s</%s>' % (
                tag, key, text, tag)
            if en_dict and text == en_dict[key][1]:
                line = '<!--%s-->' % line
            line = '  %s\n' % line
            f.write(line)
        f.write('</ResourceDictionary>\n')


if __name__ == "__main__":
    dir = os.path.dirname(os.path.abspath(__file__))
    dir = os.path.join(dir, "../OpenUtau/Strings/")
    lang_files = os.listdir(dir)
    src_file = next(filter(lambda f: f.endswith("Strings.axaml"), lang_files))
    src_file = os.path.join(dir, src_file)
    dst_files = filter(lambda f: f.endswith("axaml") and not f.endswith("Strings.axaml"), lang_files)
    dst_files = map(lambda f: os.path.join(dir, f), dst_files)

    register_all_namespaces(src_file)
    en_dict = file_to_dict(src_file)
    dict_to_file(src_file, en_dict, None)

    # Typed resources such as the per-language ui.fontfamily are values, not
    # translatable text, and an entry equal to en-US gets commented out. Keep
    # the language's own value from ever disappearing that way.
    typed = {k for k, (tag, _) in en_dict.items() if tag != TEXT}

    for dst_file in dst_files:
        dst_dict = file_to_dict(dst_file)
        clash = {k: v[1] for k, v in dst_dict.items()
                 if k in typed and v[1] == en_dict[k][1]}
        if clash:
            sys.exit(f"{dst_file}: typed resources {clash} match en-US and would be "
                     f"commented out; delete them to fall back to en-US")
        to_remove = set(dst_dict.keys()) - set(en_dict.keys())
        to_add = set(en_dict.keys()) - set(dst_dict.keys())
        [dst_dict.pop(k) for k in to_remove]
        [dst_dict.update({k: en_dict[k]}) for k in to_add]
        dict_to_file(dst_file, dst_dict, en_dict)
