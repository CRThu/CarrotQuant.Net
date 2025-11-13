import seaborn as sns
import matplotlib.pyplot as plt

data=[  [ 9.5,   2.1,   5.8,   8.0 ],
        [ 8.8,   3.3,   6.5,   7.5 ],
        [ 7.1,   4.0,   7.2,   5.1 ],
        [ 5.0,   5.5,   4.9,   3.0 ],
        [ 2.0,   1.5,   3.1,   0.5 ]
    ]

ax = sns.heatmap(data,cmap='coolwarm',annot=True, fmt=".1f")
plt.show()