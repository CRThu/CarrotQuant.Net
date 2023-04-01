
def print_xml(content, tag="output"):
    """
    This function takes in a string parameter and prints an xml tag with the content as its value.
    This function takes in a string parameter and prints an xml tag with the tag as its name.
    """
    print("<" + tag + ">" + content + "</" + tag + ">")
