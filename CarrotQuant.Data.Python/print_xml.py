import builtins
import sys
import threading


# 输出xml标签
# 输出示例和输出结果
# print_xml("Hello World!")             # <output>Hello World!</output>
# print_xml("This is a test.", "test")  # <test>This is a test.</test>
# print_xml("12345", "number")          # <number>12345</number>
def print_xml(content, tag="output"):
    """
    This function takes in a string parameter and prints an xml tag with the content as its value.
    This function takes in a string parameter and prints an xml tag with the tag as its name.
    """
    lock = threading.Lock()
    with lock:
        content = content.replace("\n", "\\n")
        content = content.replace("\r", "\\r")
        content = content.replace("\t", "\\t")
        sys.stdout.write("<" + tag + ">" + content + "</" + tag + ">\n")
        sys.stdout.flush()


# 兼容stdout的print调用
def print_compat(*args, tag="output"):
    # implementation of print_xml function
    # convert args to string and concatenate them
    result = ""
    for arg in args:
        result += str(arg)
    # return the concatenated string
    return print_xml(result, tag)


def override_print():
    builtins.print = print_compat


if __name__ == '__main__':
    print("TEST1")
    override_print()
    print("TEST2", tag="TAG")
    print("T", "E", "S", "T", "3")
    print("T", "E", "S", "T", "4", tag="TAG")
    print_xml("TEST5", "TAG")
