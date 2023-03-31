# 跨语言交互测试函数
import sys


class MyClass:
    def func0(self) -> None:
        print_xml("Function with 0 arguments")

    def func1(self, arg1: str) -> None:
        print_xml("Function with 1 argument:" + arg1)

    def func2(self, arg1: str, arg2: str) -> None:
        print_xml("Function with 2 arguments:" + arg1 + " " + arg2)

    def func3(self, arg1: str, arg2: str, arg3: str) -> None:
        print_xml("Function with 3 arguments:" + arg1 + " " + arg2 + " " + arg3)


def main() -> None:
    my_class = MyClass()

    if len(sys.argv) < 2:
        print_xml("Please provide a function name", "error")
    else:
        func_name = sys.argv[1]
        args = sys.argv[2:]

        try:
            func = getattr(my_class, func_name)
            func(*args)
        except AttributeError:
            print_xml("Function does not exist", "error")
        except Exception as e:
            print_xml(f"Exception occurred: {e}", "error")

    print_xml("0", "return")
    sys.exit()  # 调用则退出程序


def print_xml(content, tag="output"):
    """
    This function takes in a string parameter and prints an xml tag with the content as its value.
    This function takes in a string parameter and prints an xml tag with the tag as its name.
    """
    print("<" + tag + ">" + content + "</" + tag + ">")


if __name__ == '__main__':
    main()
