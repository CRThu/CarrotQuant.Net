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
        print("<" + tag + ">" + content + "</" + tag + ">")
